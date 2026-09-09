Imports System.Drawing.Printing
Imports System.IO
Imports System.Net
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Collections.Generic
Imports System.Net.Security
Imports System.Security.Cryptography.X509Certificates

Public Class frmInterface

    Public cnn_MySQL_Admin As New MySqlConnector.MySqlConnection
    Public cnn_MySQL_Central As New MySqlConnector.MySqlConnection
    Public cnn_MySQL_Local As New MySqlConnector.MySqlConnection
    Public cnn_MySQL_CentralAsync As New MySqlConnector.MySqlConnection
    Public cnn_MySQL_LocalAsync As New MySqlConnector.MySqlConnection

    Public cnn_MySQL_CentralAsyncALM As New MySqlConnector.MySqlConnection
    Public cnn_MySQL_LocalAsyncALM As New MySqlConnector.MySqlConnection

    Public CLUES As String = ""
    'Dim IdCaja As Integer = 0
    Dim cSiglas As String = ""

    Dim tb_ClienteData As DataTable
    Dim IpServidor As String = ""

    Dim load_init As Boolean = True

    Private cts As CancellationTokenSource
    Private export_hosting_tarea As Task

    ' =========================================================================
    ' Control de Estado y Reconexión Servidor Central (Protección HostGator)
    ' =========================================================================
    Private _servidorCentralConectado As Boolean = False
    Private _intentosReconexionCentral As Integer = 0
    Private _estaReconectandoCentral As Boolean = False
    Private WithEvents TimerReconexionCentral As New System.Windows.Forms.Timer()

    ' =========================================================================
    ' Control de Notificaciones a Compras (FLOWserve y DIVERSOS)
    ' =========================================================================
    Private _ultimoChequeoNotificaciones As DateTime = DateTime.MinValue
    Private _procesandoNotificaciones As Boolean = False

#Region "Propiedades"

    Protected str_FTP_USUARIO As String
    Public Property FTP_USUARIO() As String
        Get
            Return str_FTP_USUARIO
        End Get
        Set(ByVal Value As String)
            str_FTP_USUARIO = Value
        End Set
    End Property

    Protected str_FTP_PASSWORD As String
    Public Property FTP_PASSWORD() As String
        Get
            Return str_FTP_PASSWORD
        End Get
        Set(ByVal Value As String)
            str_FTP_PASSWORD = Value
        End Set
    End Property

    Protected str_FTP_IP As String
    Public Property FTP_IP() As String
        Get
            If String.IsNullOrWhiteSpace(str_FTP_IP) OrElse str_FTP_IP.Equals("ftp://", StringComparison.OrdinalIgnoreCase) OrElse str_FTP_IP.Equals("ftp:///", StringComparison.OrdinalIgnoreCase) Then
                Dim hostIp As String = If(String.IsNullOrWhiteSpace(IpServidor), "127.0.0.1", IpServidor.Trim())
                hostIp = hostIp.Replace("ftp://", "").Replace("ftps://", "").Replace("http://", "").Trim("/"c, " "c)
                If String.IsNullOrWhiteSpace(hostIp) Then hostIp = "127.0.0.1"
                Return "ftp://" & hostIp & "/"
            End If
            Return str_FTP_IP
        End Get
        Set(ByVal Value As String)
            str_FTP_IP = Value
        End Set
    End Property


#End Region

#Region "Funciones"

    Private Function HabilitarEstatusConexionCentral(ByVal valor As Boolean)
        Try
            If Me.InvokeRequired Then
                Me.Invoke(Sub() HabilitarEstatusConexionCentral(valor))
                Return Nothing
            End If

            If valor Then
                Me.btnConectarDBCentral.TextColor = Color.Green
            Else
                Me.btnConectarDBCentral.TextColor = Color.Crimson
            End If
        Catch ex As Exception
        End Try
        Return Nothing
    End Function

    Private Function HabilitarEstatusConexionLocal(ByVal valor As Boolean)
        Try
            If Me.InvokeRequired Then
                Me.Invoke(Sub() HabilitarEstatusConexionLocal(valor))
                Return Nothing
            End If

            If valor Then
                Me.btnConectarLocal.TextColor = Color.Green
            Else
                Me.btnConectarLocal.TextColor = Color.Crimson
            End If
        Catch ex As Exception
        End Try
        Return Nothing
    End Function

    ''' <summary>
    ''' Comprueba si la conexión al servidor central sigue activa y funcional.
    ''' Si detecta desconexión, marca el estado y activa la rutina de reconexión.
    ''' </summary>
    Public Function DetectarEstadoConexionCentral() As Boolean
        Try
            If cx_MySQL_Central Is Nothing OrElse cx_MySQL_Central.State <> ConnectionState.Open Then
                NotificarDesconexionCentral("Conexión central en estado cerrado o no inicializado.")
                Return False
            End If

            ' Ping ligero de comprobación
            Using cmd As New MySqlConnector.MySqlCommand("SELECT 1;", cx_MySQL_Central)
                cmd.CommandTimeout = 5
                cmd.ExecuteScalar()
            End Using

            _servidorCentralConectado = True
            HabilitarEstatusConexionCentral(True)
            Return True

        Catch ex As Exception
            NotificarDesconexionCentral("Fallo en comprobación de conexión central: " & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Notifica y cambia el estado a desconectado, e inicia la rutina de reconexión controlada.
    ''' </summary>
    Public Sub NotificarDesconexionCentral(Optional ByVal motivo As String = "")
        Dim estadoPrevioConectado As Boolean = _servidorCentralConectado
        _servidorCentralConectado = False
        HabilitarEstatusConexionCentral(False)

        Dim mensaje As String = "[Conexión Central] Se detectó desconexión del servidor central."
        If Not String.IsNullOrWhiteSpace(motivo) Then
            mensaje &= " Detalle: " & motivo
        End If

        If estadoPrevioConectado OrElse Not TimerReconexionCentral.Enabled Then
            AgregarLog(500, mensaje)
            IniciarRutinaReconexionCentral()
        End If
    End Sub

    ''' <summary>
    ''' Inicia la rutina de reconexión controlada para el servidor central.
    ''' Se ejecuta solo mientras la conexión central esté inactiva/desconectada.
    ''' </summary>
    Private Sub IniciarRutinaReconexionCentral()
        If _servidorCentralConectado Then
            DetenerRutinaReconexionCentral()
            Exit Sub
        End If

        If Not TimerReconexionCentral.Enabled Then
            ' Primer intento tras una breve pausa de 10 segundos
            TimerReconexionCentral.Interval = 10000 ' 10 segundos
            TimerReconexionCentral.Enabled = True
            AgregarLog(100, "[Reconexión Central] Rutina de reconexión activada. Primer reintento en 10 segundos...")
        End If
    End Sub

    ''' <summary>
    ''' Detiene y desactiva la rutina de reconexión una vez restablecida la conexión central.
    ''' </summary>
    Private Sub DetenerRutinaReconexionCentral()
        TimerReconexionCentral.Enabled = False
        _intentosReconexionCentral = 0
        _estaReconectandoCentral = False
    End Sub

    ''' <summary>
    ''' Cierra de manera segura cualquier conexión central existente para liberar sockets y recursos antes de reconectar.
    ''' </summary>
    Private Sub CerrarConexionesCentrales()
        Try
            If cx_MySQL_Central IsNot Nothing AndAlso cx_MySQL_Central.State <> ConnectionState.Closed Then
                cx_MySQL_Central.Close()
            End If
        Catch ex As Exception
        End Try

        Try
            If cx_MySQL_CentralAsync IsNot Nothing AndAlso cx_MySQL_CentralAsync.State <> ConnectionState.Closed Then
                cx_MySQL_CentralAsync.Close()
            End If
        Catch ex As Exception
        End Try

        Try
            If cx_MySQL_CentralAsyncALM IsNot Nothing AndAlso cx_MySQL_CentralAsyncALM.State <> ConnectionState.Closed Then
                cx_MySQL_CentralAsyncALM.Close()
            End If
        Catch ex As Exception
        End Try
    End Sub

    ''' <summary>
    ''' Temporizador de reconexión al servidor central con protección anti-bloqueo para HostGator:
    ''' - Intento 1: 10 segundos
    ''' - Intento 2: 30 segundos
    ''' - Intento 3 en adelante: 5 minutos (300,000 ms) para proteger la IP contra bloqueos en el firewall de HostGator.
    ''' </summary>
    Private Sub TimerReconexionCentral_Tick(sender As Object, e As EventArgs) Handles TimerReconexionCentral.Tick
        If _estaReconectandoCentral Then Exit Sub

        Try
            _estaReconectandoCentral = True
            TimerReconexionCentral.Enabled = False

            _intentosReconexionCentral += 1
            AgregarLog(100, "[Reconexión Central] Ejecutando intento de reconexión #" & _intentosReconexionCentral & "...")

            ' Cerrar sockets/conexiones previas antes de reintentar
            CerrarConexionesCentrales()

            Dim reconectado As Boolean = False
            If Not String.IsNullOrWhiteSpace(Me.CLUES) Then
                reconectado = Me.Conectar_Central(Me.CLUES)
            End If

            If reconectado Then
                _servidorCentralConectado = True
                _intentosReconexionCentral = 0
                _estaReconectandoCentral = False
                Me.HabilitarEstatusConexionCentral(True)

                AgregarLog(200, "[Reconexión Central] ¡Conexión con el servidor central restablecida con éxito!")

                ' Reactivar procesos de sincronización
                If Me.chkActivar.Checked Then
                    Me.ReiniciarProcesoSP()
                Else
                    Me.chkActivar.Checked = True
                End If

                ' Desactivar rutina de reconexión ya que la conexión está restablecida
                DetenerRutinaReconexionCentral()
                Exit Sub
            Else
                ' Falló el intento: Programar siguiente según la política de HostGator
                Dim proximoIntervaloMs As Integer = 300000 ' 5 minutos (300,000 ms)
                Dim textoIntervalo As String = "5 minutos (Protección Anti-Bloqueo HostGator activa)"

                If _intentosReconexionCentral = 1 Then
                    ' Si falló el intento 1, reintento 2 en 30 segundos
                    proximoIntervaloMs = 30000 ' 30 segundos
                    textoIntervalo = "30 segundos"
                Else
                    ' A partir del 2do intento fallido (para intento 3 en adelante):
                    ' HostGator bloquea IPs por conexiones fallidas repetidas. Reintento cada 5 minutos.
                    proximoIntervaloMs = 300000 ' 5 minutos
                    textoIntervalo = "5 minutos (Protección Anti-Bloqueo HostGator activa)"
                End If

                AgregarLog(500, "[Reconexión Central] Intento #" & _intentosReconexionCentral & " fallido. Próximo intento programado en " & textoIntervalo & ".")

                _estaReconectandoCentral = False
                TimerReconexionCentral.Interval = proximoIntervaloMs
                TimerReconexionCentral.Enabled = True
            End If

        Catch ex As Exception
            _estaReconectandoCentral = False
            TimerReconexionCentral.Interval = 300000 ' 5 minutos ante excepciones
            TimerReconexionCentral.Enabled = True
            AgregarLog(500, "[Reconexión Central] Error en proceso de reconexión: " & ex.Message & ". Reintentando en 5 minutos.")
        End Try
    End Sub

    Private Function Conectar_Central(clues) As Boolean

        Try

            Dim Database As String = ""
            Dim Uid As String = ""
            Dim Pwd As String = ""

            ' ==================================================================================================================================
            Dim cadena_conexion_admin As String = "Server=histomedic.mx;Database=mirtheda_admin;Uid=mirtheda_root;Pwd=Bsapmd2cKb*5;SSL Mode=None;"
            If cx_MySQL_Admin.State = ConnectionState.Closed Then
                If Not Test_MySQL_Admin(cadena_conexion_admin) Then
                    _servidorCentralConectado = False
                    Me.HabilitarEstatusConexionCentral(False)
                    Return False
                End If
            End If

            tb_ClienteData = tb_Recordset_MySQL_Admin("SELECT * FROM ssf_clientes WHERE clues = '" & clues & "'")
            If tb_ClienteData Is Nothing OrElse tb_ClienteData.Rows.Count = 0 Then
                AgregarLog(500, ".Error de Conexión con el Servidor (Cliente no encontrado). ")
                _servidorCentralConectado = False
                Me.HabilitarEstatusConexionCentral(False)
                Return False
            End If

            Database = tb_ClienteData.Rows(0).Item("db_name").ToString
            Uid = tb_ClienteData.Rows(0).Item("db_user").ToString
            Pwd = tb_ClienteData.Rows(0).Item("db_pass").ToString

            If tb_ClienteData.Rows(0).Item("actualizaciones").ToString <> "SI" Then
                AgregarLog(500, "No Disponible para actualizaciones. ")
                _servidorCentralConectado = False
                Me.HabilitarEstatusConexionCentral(False)
                Return False
            End If

            ' ==================================================================================================================================

            Dim cadena_conexion As String = "Server=lfmcontrol.com.mx;Database=" & Database & ";Uid=" & Uid & ";Pwd=" & Pwd & ";SSL Mode=None;"

            CerrarConexionesCentrales()

            If Test_MySQL_Central(cadena_conexion) Then
                Test_MySQL_CentralAsync(cadena_conexion)
                Test_MySQL_CentralAsyncALM(cadena_conexion)
                _servidorCentralConectado = True
                Me.HabilitarEstatusConexionCentral(True)
                DetenerRutinaReconexionCentral()
                Return True
            Else
                _servidorCentralConectado = False
                Me.HabilitarEstatusConexionCentral(False)
                Return False
            End If

        Catch ex As Exception
            _servidorCentralConectado = False
            Me.HabilitarEstatusConexionCentral(False)
            AgregarLog(500, ex.Message & ". Error al conectar a Servidor Central")
            Return False
        End Try

    End Function

    Private Function Conectar_Local() As Boolean

        If cx_MySQL_local.State = ConnectionState.Open Then
            Me.HabilitarEstatusConexionLocal(True)
            Return True
        End If

        If cx_MySQL_local.State = ConnectionState.Closed Then
            If Test_MySQL_local("server=" & IpServidor & ";Port=1865;uid=root;pwd=Bhytmd2cKb*5;database=histoclin") Then
                Test_MySQL_localAsync("server=" & IpServidor & ";Port=1865;uid=root;pwd=Bhytmd2cKb*5;database=histoclin")
                Test_MySQL_localAsyncALM("server=" & IpServidor & ";Port=1865;uid=root;pwd=Bhytmd2cKb*5;database=histoclin")
                Me.HabilitarEstatusConexionLocal(True)
                Return True
            End If
        End If

        Me.HabilitarEstatusConexionLocal(False)
        Return False

    End Function

    ''' <summary>
    ''' Obtiene la ip del servidor desde el registro de windows en: "LOCAL_MACHINE\SOFTWARE\HISTOCLIN\IpServidor" al cual apunta el sistema.
    ''' </summary>
    ''' <remarks></remarks>
    Private Sub Get_IpServidor()

        Dim key_Clave As Microsoft.Win32.RegistryKey
        Try
            key_Clave = Microsoft.Win32.Registry.LocalMachine.OpenSubKey("SOFTWARE\histomedic\")
            If key_Clave IsNot Nothing Then
                Dim val As Object = key_Clave.GetValue("IpServidor", "127.0.0.1")
                If val IsNot Nothing Then
                    IpServidor = val.ToString().Trim()
                End If
            End If
        Catch ex As Exception
        End Try

        If String.IsNullOrWhiteSpace(IpServidor) Then
            IpServidor = "127.0.0.1"
        End If

    End Sub

    Private Function Calcula_FechaActual() As Date

        Dim fecha As String
        fecha = Format(Today, "dd/MM/yyyy") & " " & Format(TimeOfDay, "HH:mm:ss")

        Return CDate(fecha)

    End Function

    Private Sub IniciarRutina()

        Try

            TimerEnlace.Enabled = False

            '======================================
            ' Rutinas de Sincronización
            ' Solo se ejecutan si el servidor central se encuentra conectado
            '======================================
            If _servidorCentralConectado Then
                '== Exportar Archivos Adjuntos 
                Try
                    Me.ExportarAdjuntos()
                Catch ex As Exception
                    LogEventos.Escribir("ExportarAdjuntos. " & ex.Message)
                End Try

                Try
                    Me.ExportarAdjuntosAlmacen()
                Catch ex As Exception
                    LogEventos.Escribir("ExportarAdjuntosAlmacen. " & ex.Message)
                End Try

                Try
                    Me.ExportarAdjuntosFotosMaterial()
                Catch ex As Exception
                    LogEventos.Escribir("ExportarAdjuntosFotosMaterial. " & ex.Message)
                End Try

                ' == Continuamente verificando si la tarea en segundo plano está activa
                ReiniciarProcesoSP()
            Else
                ' Si la conexión al central está caída, asegurar que la rutina de reconexión esté activa
                IniciarRutinaReconexionCentral()
            End If

            ' ======================================
            ' Notificaciones Automáticas a Compras (FLOWserve y DIVERSOS)
            ' ======================================
            Try
                If Not _procesandoNotificaciones AndAlso (DateTime.Now.Subtract(_ultimoChequeoNotificaciones).TotalMinutes >= 15) Then
                    _ultimoChequeoNotificaciones = DateTime.Now
                    Me.NotificarCotizacionesPendientesFlowserve()
                    Me.NotificarCotizacionesPendientesDiversos()
                End If
            Catch exNotif As Exception
                LogEventos.Escribir("Error en ciclo de notificaciones automáticas: " & exNotif.Message)
            End Try

            ' ======================================
            ' Gestión de memoria del log en pantalla (mantener historial limpio sin reiniciar app)
            ' ======================================
            Try
                If lstLog.Items.Count > 100 Then
                    While lstLog.Items.Count > 50
                        lstLog.Items.RemoveAt(0)
                    End While
                End If
            Catch ex As Exception
            End Try

            TimerEnlace.Enabled = True

        Catch ex As Exception
            TimerEnlace.Enabled = True
            LogEventos.Escribir("Error General. " & ex.Message)
        End Try

    End Sub

    Private Sub ExportarDataToHostingSP_Load(token As CancellationToken)

        While Not token.IsCancellationRequested
            If _servidorCentralConectado Then
                Me.ExportarDataToHostingSP()
                Me.ExportarDataToHostingSP_Almacen()
            End If
            token.WaitHandle.WaitOne(1000)
        End While

    End Sub

    Private Sub ExportarDataToHostingSP()

        Try

            Dim limittext As String = Me.txtLimitRegistros.Text.Trim
            If Len(Me.txtLimitRegistros.Text) = 0 Then
                limittext = 2000
            End If

            Dim tb_his_replica_local As DataTable
            tb_his_replica_local = tb_Recordset_MySQL_localAsync("Select * from his_replica " & _
                                                                 "WHERE " & _
                                                                 "tabla_afectada NOT IN (" & _
                                                                 "" & GetQuery_TablasAlmacen().Trim & "" & _
                                                                 ") and " & _
                                                                 "sinc = 1 " & _
                                                                 "order by id LIMIT " & limittext & "")

            Dim tb_temp_insert As DataTable
            If tb_his_replica_local.Rows.Count = 0 Then
                Update_CentralAsync("fchactual", _
                               "fchActual = current_timestamp", _
                               "Id", 1)
                Exit Sub
            End If

            Dim campo_llave As String = ""
            Dim campo_llave_value_id As Integer = 0
            Dim fecha_str As String = ""
            Dim campo_nombre As String = ""

            ' Cache de Esquemas y Llaves Primarias para optimizar el rendimiento
            Dim columnCache As New Dictionary(Of String, DataTable)()
            Dim primaryKeyCache As New Dictionary(Of String, String)()

            Try
                If lstLog.InvokeRequired Then
                    lstLog.Invoke(Sub()
                                      Me.ProgressBarX_SP.Minimum = 0
                                      Me.ProgressBarX_SP.Maximum = tb_his_replica_local.Rows.Count
                                      Me.ProgressBarX_SP.Text = "0 de " & tb_his_replica_local.Rows.Count
                                  End Sub)
                Else
                    Me.ProgressBarX_SP.Minimum = 0
                    Me.ProgressBarX_SP.Maximum = tb_his_replica_local.Rows.Count
                    Me.ProgressBarX_SP.Text = "0 de " & tb_his_replica_local.Rows.Count
                End If
            Catch ex2 As Exception
            End Try

            Dim set_value_row As String = ""
            Dim longblob_filed_contain As Boolean = False
            Dim name_file_blob As String = ""

            For i As Integer = 0 To tb_his_replica_local.Rows.Count - 1

                longblob_filed_contain = False

                Dim table_name As String = tb_his_replica_local.Rows(i).Item("tabla_afectada").ToString

                Dim set_value As String = ""

                ' Obtener llave primaria de cachÃ© o de BD
                If Not primaryKeyCache.TryGetValue(table_name, campo_llave) Then
                    Dim tb_campo_llave As DataTable = tb_Recordset_MySQL_localAsync("SHOW COLUMNS FROM " & table_name & " WHERE Extra = 'auto_increment';")
                    If tb_campo_llave.Rows.Count > 0 Then
                        campo_llave = tb_campo_llave.Rows(0).Item("Field").ToString
                    Else
                        campo_llave = ""
                    End If
                    primaryKeyCache(table_name) = campo_llave
                End If

                If tb_his_replica_local.Rows(i).Item("tipo_operacion").ToString = "INSERT" Then

                    campo_llave_value_id = tb_his_replica_local.Rows(i).Item("id_tabla_afectada").ToString
                    tb_temp_insert = tb_Recordset_MySQL_localAsync("Select * from " & table_name & " where " & campo_llave & " = " & campo_llave_value_id & "")

                    If tb_temp_insert.Rows.Count > 0 Then

                        Dim tb_campos As DataTable = Nothing
                        If Not columnCache.TryGetValue(table_name, tb_campos) Then
                            tb_campos = tb_Recordset_MySQL_localAsync("SHOW COLUMNS FROM " & table_name & ";")
                            columnCache(table_name) = tb_campos
                        End If

                        For col As Integer = 0 To tb_campos.Rows.Count - 1

                            campo_nombre = tb_campos.Rows(col).Item("Field").ToString

                            Dim tipo As String = ""
                            tipo = tb_campos.Rows(col).Item("Type")
                            Dim texto_buscar As String = tipo
                            If texto_buscar.ToLower().Contains("int") Then
                                tipo = "int"
                            End If

                            If tipo = "date" Then
                                Try
                                    fecha_str = "'" & Format(CDate(tb_temp_insert.Rows(0).Item(col).ToString), "yyyy-MM-dd") & "'"
                                Catch ex As Exception
                                    fecha_str = "null"
                                End Try
                                set_value = set_value & " " & campo_nombre & " = " & fecha_str & ", "

                            ElseIf tipo = "datetime" Then
                                Try
                                    fecha_str = "'" & Format(CDate(tb_temp_insert.Rows(0).Item(col).ToString), "yyyy-MM-dd HH:mm:ss") & "'"
                                Catch ex As Exception
                                    fecha_str = "null"
                                End Try
                                set_value = set_value & " " & campo_nombre & " = " & fecha_str & ", "

                            ElseIf tipo = "longblob" Then
                                longblob_filed_contain = True

                            ElseIf tipo = "int" Then
                                Dim campo_int As String = ""
                                Try
                                    If tb_temp_insert.Rows(0).Item(col).ToString = "" Then
                                        campo_int = "null"
                                    Else
                                        campo_int = tb_temp_insert.Rows(0).Item(col).ToString
                                    End If
                                Catch ex As Exception
                                    campo_int = "null"
                                End Try
                                set_value = set_value & " " & campo_nombre & " = " & campo_int & ", "

                            Else

                                set_value_row = ""
                                set_value_row = tb_temp_insert.Rows(0).Item(col).ToString
                                set_value_row = set_value_row.Replace("'", "\'")
                                set_value = set_value & " " & campo_nombre & " = '" & set_value_row & "', "
                            End If

                        Next

                        Dim caracterARemover As Char = " "
                        set_value = set_value.TrimEnd(New Char() {caracterARemover})

                        Dim caracterARemover_coma As Char = ","
                        set_value = set_value.TrimEnd(New Char() {caracterARemover_coma})

                    End If

                    If set_value = "" Then
                        Update_localAsync("his_replica", _
                                     "sinc = 0", _
                                     "id", tb_his_replica_local.Rows(i).Item("id").ToString)
                    Else
                        If Insert_CentralAsync(table_name, _
                                          set_value) Then

                            If longblob_filed_contain = True Then
                                Dim tb_longblob As DataTable = tb_Recordset_MySQL_localAsync("SHOW COLUMNS FROM " & table_name & " WHERE Type = 'longblob';")
                                For col_lb As Integer = 0 To tb_longblob.Rows.Count - 1
                                    name_file_blob = tb_longblob.Rows(col_lb).Item("Field").ToString
                                    If Not IsDBNull(tb_temp_insert.Rows(0).Item(name_file_blob)) Then
                                        Try
                                            Update_FotoSistema_CentralAsync(name_file_blob, table_name, campo_llave, campo_llave_value_id, tb_temp_insert.Rows(0).Item(name_file_blob))
                                        Catch ex As Exception
                                            AgregarLog(500, "Error desconocido en Exportar Data: " & ex.Message)
                                        End Try
                                    End If
                                Next

                            End If

                            Update_CentralAsync("fchactual", _
                                           "fchActual = current_timestamp", _
                                           "Id", 1)

                            Update_localAsync("his_replica", _
                                         "sinc = 0", _
                                         "id", tb_his_replica_local.Rows(i).Item("id").ToString)

                        Else

                            Dim tb_valida_ins As DataTable
                            tb_valida_ins = tb_Recordset_MySQL_CentralAsync("Select " & campo_llave & " from " & table_name & " WHERE " & campo_llave & " = '" & campo_llave_value_id & "'")

                            If tb_valida_ins.Rows.Count > 0 Then
                                Update_localAsync("his_replica", _
                                             "sinc = 0", _
                                             "id", tb_his_replica_local.Rows(i).Item("id").ToString)
                            End If

                        End If

                    End If


                ElseIf tb_his_replica_local.Rows(i).Item("tipo_operacion").ToString = "UPDATE" Then

                    campo_llave_value_id = tb_his_replica_local.Rows(i).Item("id_tabla_afectada").ToString
                    tb_temp_insert = tb_Recordset_MySQL_localAsync("Select * from " & table_name & " where " & campo_llave & " = " & campo_llave_value_id & "")

                    If tb_temp_insert.Rows.Count > 0 Then

                        Dim tb_campos As DataTable = Nothing
                        If Not columnCache.TryGetValue(table_name, tb_campos) Then
                            tb_campos = tb_Recordset_MySQL_localAsync("SHOW COLUMNS FROM " & table_name & ";")
                            columnCache(table_name) = tb_campos
                        End If

                        'Diccionario con las columnas excluidas por tabla
                        Dim columnasExcluidas As New Dictionary(Of String, HashSet(Of String))(StringComparer.OrdinalIgnoreCase) From {
                            {
                                "tb_pedidos_cliente_adjuntos",
                                New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
                                    "sinc"
                                }
                            },
                              {
                                "tb_compras_cotizaciones_adjuntos",
                                New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
                                    "sinc"
                                }
                            },
                              {
                                "tb_compras_cotizacion_interna_adjuntos",
                                New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
                                    "sinc"
                                }
                            },
                              {
                                "tb_pedidos_proveedor_adjuntos",
                                New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
                                    "sinc"
                                }
                            },
                              {
                                "tb_ventas_cotizacion_cliente_adjuntos",
                                New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
                                    "sinc"
                                }
                            },
                              {
                                "tb_ventas_adjuntos",
                                New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
                                    "sinc"
                                }
                            },
                              {
                                "tb_ventas_seguimiento",
                                New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
                                    "sinc"
                                }
                            }
                        }

                        Dim columnasExcluir As HashSet(Of String) = Nothing

                        If Not columnasExcluidas.TryGetValue(table_name, columnasExcluir) Then
                            'Si la tabla no existe en el diccionario, no excluye ninguna columna
                            columnasExcluir = New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
                        End If


                        For col As Integer = 0 To tb_campos.Rows.Count - 1

                            campo_nombre = tb_campos.Rows(col).Item("Field").ToString

                            'Excluir columnas especÃ­ficas de tb_servicios_sistemas
                            If columnasExcluir.Contains(campo_nombre) Then
                                Continue For
                            End If

                            Dim tipo As String = ""
                            tipo = tb_campos.Rows(col).Item("Type")
                            Dim texto_buscar As String = tipo
                            If texto_buscar.ToLower().Contains("int") Then
                                tipo = "int"
                            End If

                            If tipo = "date" Then
                                Try
                                    fecha_str = "'" & Format(CDate(tb_temp_insert.Rows(0).Item(col).ToString), "yyyy-MM-dd") & "'"
                                Catch ex As Exception
                                    fecha_str = "null"
                                End Try
                                set_value = set_value & " " & campo_nombre & " = " & fecha_str & ", "

                            ElseIf tipo = "datetime" Then
                                Try
                                    fecha_str = "'" & Format(CDate(tb_temp_insert.Rows(0).Item(col).ToString), "yyyy-MM-dd HH:mm:ss") & "'"
                                Catch ex As Exception
                                    fecha_str = "null"
                                End Try
                                set_value = set_value & " " & campo_nombre & " = " & fecha_str & ", "

                            ElseIf tipo = "longblob" Then

                                If Not IsDBNull(tb_temp_insert.Rows(0).Item(col)) Then
                                    Try
                                        Update_FotoSistema_CentralAsync(campo_nombre, table_name, campo_llave, campo_llave_value_id, tb_temp_insert.Rows(0).Item(col))
                                    Catch ex As Exception
                                        AgregarLog(500, "Error desconocido en Exportar Data: " & ex.Message)
                                    End Try
                                End If

                            ElseIf tipo = "int" Then

                                Dim campo_int As String = ""
                                Try

                                    If tb_temp_insert.Rows(0).Item(col).ToString = "" Then
                                        campo_int = "null"
                                    Else
                                        campo_int = tb_temp_insert.Rows(0).Item(col).ToString
                                    End If
                                Catch ex As Exception
                                    campo_int = "null"
                                End Try
                                set_value = set_value & " " & campo_nombre & " = " & campo_int & ", "

                            Else

                                set_value_row = ""
                                set_value_row = tb_temp_insert.Rows(0).Item(col).ToString
                                set_value_row = set_value_row.Replace("'", "\'")
                                set_value = set_value & " " & campo_nombre & " = '" & set_value_row & "', "
                            End If

                        Next

                        Dim caracterARemover As Char = " "
                        set_value = set_value.TrimEnd(New Char() {caracterARemover})

                        Dim caracterARemover_coma As Char = ","
                        set_value = set_value.TrimEnd(New Char() {caracterARemover_coma})

                    End If

                    If set_value = "" Then
                        Update_localAsync("his_replica", _
                                     "sinc = 0", _
                                     "id", tb_his_replica_local.Rows(i).Item("id").ToString)

                    Else
                        If Update_CentralAsync(table_name, _
                                       set_value, _
                                       campo_llave, campo_llave_value_id) Then

                            Update_CentralAsync("fchactual", _
                                           "fchActual = current_timestamp", _
                                           "Id", 1)

                            Update_localAsync("his_replica", _
                                         "sinc = 0", _
                                         "id", tb_his_replica_local.Rows(i).Item("id").ToString)
                        End If
                    End If


                ElseIf tb_his_replica_local.Rows(i).Item("tipo_operacion").ToString = "DELETE" Then

                    campo_llave_value_id = tb_his_replica_local.Rows(i).Item("id_tabla_afectada").ToString

                    If Delete_CentralAsync("DELETE FROM " & table_name & " WHERE " & campo_llave & " = " & campo_llave_value_id) Then

                        Update_CentralAsync("fchactual", _
                                       "fchActual = current_timestamp", _
                                       "Id", 1)

                        Update_localAsync("his_replica", _
                                     "sinc = 0", _
                                     "id", tb_his_replica_local.Rows(i).Item("id").ToString)
                    End If

                End If

                ' Optimización del progreso de la barra de progreso (throttling de UI)
                Dim current_val As Integer = i + 1
                If i Mod 10 = 0 OrElse i = tb_his_replica_local.Rows.Count - 1 Then
                    Try
                        If ProgressBarX_SP.InvokeRequired Then
                            ProgressBarX_SP.Invoke(Sub()
                                                       Me.ProgressBarX_SP.Value = current_val
                                                       Me.ProgressBarX_SP.Text = current_val.ToString() & " de " & tb_his_replica_local.Rows.Count
                                                   End Sub)
                        Else
                            Me.ProgressBarX_SP.Value = current_val
                            Me.ProgressBarX_SP.Text = current_val.ToString() & " de " & tb_his_replica_local.Rows.Count
                        End If
                    Catch ex2 As Exception
                    End Try
                End If

            Next

            '== Eliminar Cargados === 
            Delete_localAsync("DELETE from his_replica " & _
                                "WHERE " & _
                                "tabla_afectada NOT IN (" & _
                                "" & GetQuery_TablasAlmacen() & "" & _
                                ") and " & _
                                "sinc = 0 ")
            '== Eliminar Cargados === 

            Try
                If ProgressBarX_SP.InvokeRequired Then
                    ProgressBarX_SP.Invoke(Sub() Me.ProgressBarX_SP.Value = 0)
                Else
                    Me.ProgressBarX_SP.Value = 0
                End If
            Catch ex2 As Exception
            End Try

        Catch ex As Exception
            AgregarLog(500, "Error desconocido en ExportarDataToHosting: " & ex.Message)
        End Try

    End Sub

    Private Sub ExportarDataToHostingSP_Almacen()

        Try

            Dim limittext As String = Me.txtLimitRegistros.Text.Trim
            If Len(Me.txtLimitRegistros.Text) = 0 Then
                limittext = 2000
            End If

            Dim tb_his_replica_local As DataTable
            tb_his_replica_local = tb_Recordset_MySQL_localAsyncALM("Select * from his_replica " & _
                                                                 "WHERE " & _
                                                                 "tabla_afectada IN (" & _
                                                                 "" & GetQuery_TablasAlmacen() & "" & _
                                                                 ") and " & _
                                                                 "sinc = 1 " & _
                                                                 "order by id LIMIT " & limittext & "")
            Dim tb_temp_insert As DataTable
            If tb_his_replica_local.Rows.Count = 0 Then
                Update_CentralAsyncALM("fchactual", _
                               "fchActual = current_timestamp", _
                               "Id", 1)
                Exit Sub
            End If

            Dim campo_llave As String = ""
            Dim campo_llave_value_id As Integer = 0
            Dim fecha_str As String = ""
            Dim campo_nombre As String = ""


            Try
                If ProgressBarX_SPALM.InvokeRequired Then
                    lstLog.Invoke(Sub() Me.ProgressBarX_SPALM.Minimum = 0)
                    lstLog.Invoke(Sub() Me.ProgressBarX_SPALM.Maximum = tb_his_replica_local.Rows.Count)
                    lstLog.Invoke(Sub() Me.ProgressBarX_SPALM.Text = "0 de " & tb_his_replica_local.Rows.Count)
                Else
                    Me.ProgressBarX_SPALM.Minimum = 0
                    Me.ProgressBarX_SPALM.Maximum = tb_his_replica_local.Rows.Count
                    Me.ProgressBarX_SPALM.Text = "0 de " & tb_his_replica_local.Rows.Count
                End If
            Catch ex2 As Exception
            End Try

            Dim set_value_row As String = ""
            Dim longblob_filed_contain As Boolean = False
            Dim name_file_blob As String = ""

            Dim cache_campo_llave As New Dictionary(Of String, String)()
            Dim cache_campos As New Dictionary(Of String, DataTable)()
            Dim cache_longblob As New Dictionary(Of String, DataTable)()
            Dim actualizoFchActual As Boolean = False

            For i As Integer = 0 To tb_his_replica_local.Rows.Count - 1

                longblob_filed_contain = False

                Dim table_name As String = tb_his_replica_local.Rows(i).Item("tabla_afectada").ToString

                Dim set_value As String = ""

                If Not cache_campo_llave.TryGetValue(table_name, campo_llave) Then
                    Dim tb_campo_llave As DataTable = tb_Recordset_MySQL_localAsyncALM("SHOW COLUMNS FROM " & table_name & " WHERE Extra = 'auto_increment';")
                    If tb_campo_llave.Rows.Count > 0 Then
                        campo_llave = tb_campo_llave.Rows(0).Item("Field").ToString()
                    End If
                    cache_campo_llave(table_name) = campo_llave
                End If

                If tb_his_replica_local.Rows(i).Item("tipo_operacion").ToString = "INSERT" Then

                    campo_llave_value_id = tb_his_replica_local.Rows(i).Item("id_tabla_afectada").ToString
                    tb_temp_insert = tb_Recordset_MySQL_localAsyncALM("Select * from " & table_name & " where " & campo_llave & " = " & campo_llave_value_id & "")

                    If tb_temp_insert.Rows.Count > 0 Then

                        Dim tb_campos As DataTable = Nothing
                        If Not cache_campos.TryGetValue(table_name, tb_campos) Then
                            tb_campos = tb_Recordset_MySQL_localAsyncALM("SHOW COLUMNS FROM " & table_name & ";")
                            cache_campos(table_name) = tb_campos
                        End If

                        For col As Integer = 0 To tb_campos.Rows.Count - 1

                            campo_nombre = tb_campos.Rows(col).Item("Field").ToString

                            Dim tipo As String = ""
                            tipo = tb_campos.Rows(col).Item("Type")
                            Dim texto_buscar As String = tipo
                            If texto_buscar.ToLower().Contains("int") Then
                                tipo = "int"
                            End If

                            If tipo = "date" Then
                                Try
                                    fecha_str = "'" & Format(CDate(tb_temp_insert.Rows(0).Item(col).ToString), "yyyy-MM-dd") & "'"
                                Catch ex As Exception
                                    fecha_str = "null"
                                End Try
                                set_value = set_value & " " & campo_nombre & " = " & fecha_str & ", "

                            ElseIf tipo = "datetime" Then
                                Try
                                    fecha_str = "'" & Format(CDate(tb_temp_insert.Rows(0).Item(col).ToString), "yyyy-MM-dd HH:mm:ss") & "'"
                                Catch ex As Exception
                                    fecha_str = "null"
                                End Try
                                set_value = set_value & " " & campo_nombre & " = " & fecha_str & ", "

                            ElseIf tipo = "longblob" Then
                                longblob_filed_contain = True

                            ElseIf tipo = "int" Then
                                Dim campo_int As String = ""
                                Try
                                    If tb_temp_insert.Rows(0).Item(col).ToString = "" Then
                                        campo_int = "null"
                                    Else
                                        campo_int = tb_temp_insert.Rows(0).Item(col).ToString
                                    End If
                                Catch ex As Exception
                                    campo_int = "null"
                                End Try
                                set_value = set_value & " " & campo_nombre & " = " & campo_int & ", "

                            Else

                                set_value_row = ""
                                set_value_row = tb_temp_insert.Rows(0).Item(col).ToString
                                set_value_row = set_value_row.Replace("'", "\'")
                                set_value = set_value & " " & campo_nombre & " = '" & set_value_row & "', "
                            End If

                        Next

                        Dim caracterARemover As Char = " "
                        set_value = set_value.TrimEnd(New Char() {caracterARemover})

                        Dim caracterARemover_coma As Char = ","
                        set_value = set_value.TrimEnd(New Char() {caracterARemover_coma})

                    End If

                    If set_value = "" Then
                        Update_localAsyncALM("his_replica", _
                                     "sinc = 0", _
                                     "id", tb_his_replica_local.Rows(i).Item("id").ToString)
                    Else
                        If Insert_CentralAsyncALM(table_name, _
                                           set_value) Then

                            If longblob_filed_contain = True Then
                                Dim tb_longblob As DataTable = Nothing
                                If Not cache_longblob.TryGetValue(table_name, tb_longblob) Then
                                    tb_longblob = tb_Recordset_MySQL_localAsyncALM("SHOW COLUMNS FROM " & table_name & " WHERE Type = 'longblob';")
                                    cache_longblob(table_name) = tb_longblob
                                End If
                                For col_lb As Integer = 0 To tb_longblob.Rows.Count - 1
                                    name_file_blob = tb_longblob.Rows(col_lb).Item("Field").ToString
                                    If Not IsDBNull(tb_temp_insert.Rows(0).Item(name_file_blob)) Then
                                        Try
                                            Update_FotoSistema_CentralAsyncALM(name_file_blob, table_name, campo_llave, campo_llave_value_id, tb_temp_insert.Rows(0).Item(name_file_blob))
                                        Catch ex As Exception
                                            AgregarLog(500, "Error desconocido en Exportar Data: " & ex.Message)
                                        End Try
                                    End If
                                Next

                            End If

                            actualizoFchActual = True

                            Update_localAsyncALM("his_replica", _
                                         "sinc = 0", _
                                         "id", tb_his_replica_local.Rows(i).Item("id").ToString)

                        Else

                            Dim tb_valida_ins As DataTable
                            tb_valida_ins = tb_Recordset_MySQL_CentralAsyncALM("Select " & campo_llave & " from " & table_name & " WHERE " & campo_llave & " = '" & campo_llave_value_id & "'")

                            If tb_valida_ins.Rows.Count > 0 Then
                                Update_localAsyncALM("his_replica", _
                                             "sinc = 0", _
                                             "id", tb_his_replica_local.Rows(i).Item("id").ToString)
                            End If

                        End If

                    End If


                ElseIf tb_his_replica_local.Rows(i).Item("tipo_operacion").ToString = "UPDATE" Then

                    campo_llave_value_id = tb_his_replica_local.Rows(i).Item("id_tabla_afectada").ToString
                    tb_temp_insert = tb_Recordset_MySQL_localAsyncALM("Select * from " & table_name & " where " & campo_llave & " = " & campo_llave_value_id & "")

                    If tb_temp_insert.Rows.Count > 0 Then

                        Dim tb_campos As DataTable = Nothing
                        If Not cache_campos.TryGetValue(table_name, tb_campos) Then
                            tb_campos = tb_Recordset_MySQL_localAsyncALM("SHOW COLUMNS FROM " & table_name & ";")
                            cache_campos(table_name) = tb_campos
                        End If

                        For col As Integer = 0 To tb_campos.Rows.Count - 1

                            campo_nombre = tb_campos.Rows(col).Item("Field").ToString

                            Dim tipo As String = ""
                            tipo = tb_campos.Rows(col).Item("Type")
                            Dim texto_buscar As String = tipo
                            If texto_buscar.ToLower().Contains("int") Then
                                tipo = "int"
                            End If

                            If tipo = "date" Then
                                Try
                                    fecha_str = "'" & Format(CDate(tb_temp_insert.Rows(0).Item(col).ToString), "yyyy-MM-dd") & "'"
                                Catch ex As Exception
                                    fecha_str = "null"
                                End Try
                                set_value = set_value & " " & campo_nombre & " = " & fecha_str & ", "

                            ElseIf tipo = "datetime" Then
                                Try
                                    fecha_str = "'" & Format(CDate(tb_temp_insert.Rows(0).Item(col).ToString), "yyyy-MM-dd HH:mm:ss") & "'"
                                Catch ex As Exception
                                    fecha_str = "null"
                                End Try
                                set_value = set_value & " " & campo_nombre & " = " & fecha_str & ", "

                            ElseIf tipo = "longblob" Then

                                If Not IsDBNull(tb_temp_insert.Rows(0).Item(col)) Then
                                    Try
                                        Update_FotoSistema_CentralAsyncALM(campo_nombre, table_name, campo_llave, campo_llave_value_id, tb_temp_insert.Rows(0).Item(col))
                                    Catch ex As Exception
                                        AgregarLog(500, "Error desconocido en Exportar Data: " & ex.Message)
                                    End Try
                                End If

                            ElseIf tipo = "int" Then
                                Dim campo_int As String = ""
                                Try
                                    If tb_temp_insert.Rows(0).Item(col).ToString = "" Then
                                        campo_int = "null"
                                    Else
                                        campo_int = tb_temp_insert.Rows(0).Item(col).ToString
                                    End If
                                Catch ex As Exception
                                    campo_int = "null"
                                End Try
                                set_value = set_value & " " & campo_nombre & " = " & campo_int & ", "

                            Else

                                set_value_row = ""
                                set_value_row = tb_temp_insert.Rows(0).Item(col).ToString
                                set_value_row = set_value_row.Replace("'", "\'")
                                set_value = set_value & " " & campo_nombre & " = '" & set_value_row & "', "
                            End If

                        Next

                        Dim caracterARemover As Char = " "
                        set_value = set_value.TrimEnd(New Char() {caracterARemover})

                        Dim caracterARemover_coma As Char = ","
                        set_value = set_value.TrimEnd(New Char() {caracterARemover_coma})

                    End If

                    If set_value = "" Then
                        Update_localAsyncALM("his_replica", _
                                     "sinc = 0", _
                                     "id", tb_his_replica_local.Rows(i).Item("id").ToString)

                    Else
                        If Update_CentralAsyncALM(table_name, _
                                       set_value, _
                                       campo_llave, campo_llave_value_id) Then

                            actualizoFchActual = True

                            Update_localAsyncALM("his_replica", _
                                         "sinc = 0", _
                                         "id", tb_his_replica_local.Rows(i).Item("id").ToString)
                        End If
                    End If


                ElseIf tb_his_replica_local.Rows(i).Item("tipo_operacion").ToString = "DELETE" Then

                    campo_llave_value_id = tb_his_replica_local.Rows(i).Item("id_tabla_afectada").ToString

                    If Delete_CentralAsyncALM("DELETE FROM " & table_name & " WHERE " & campo_llave & " = " & campo_llave_value_id) Then

                        actualizoFchActual = True

                        Update_localAsyncALM("his_replica", _
                                     "sinc = 0", _
                                     "id", tb_his_replica_local.Rows(i).Item("id").ToString)
                    End If

                End If

                Try
                    If ProgressBarX_SPALM.InvokeRequired Then
                        ProgressBarX_SPALM.Invoke(Sub() Me.ProgressBarX_SPALM.Value = Me.ProgressBarX_SPALM.Value + 1)
                        ProgressBarX_SPALM.Invoke(Sub() Me.ProgressBarX_SPALM.Text = "" & Me.ProgressBarX_SPALM.Value & " de " & tb_his_replica_local.Rows.Count)
                    Else
                        Me.ProgressBarX_SPALM.Value = Me.ProgressBarX_SPALM.Value + 1
                        Me.ProgressBarX_SPALM.Text = "" & Me.ProgressBarX_SPALM.Value & " de " & tb_his_replica_local.Rows.Count
                    End If
                Catch ex2 As Exception
                End Try

            Next

            If actualizoFchActual Then
                Update_CentralAsyncALM("fchactual", _
                               "fchActual = current_timestamp", _
                               "Id", 1)
            End If

            '== Eliminar Cargados === 
            Delete_localAsync("DELETE from his_replica " & _
                                "WHERE " & _
                                "tabla_afectada IN (" & _
                                "" & GetQuery_TablasAlmacen() & "" & _
                                ") and " & _
                                "sinc = 0 ")
            '== Eliminar Cargados === 

            Try
                If ProgressBarX_SPALM.InvokeRequired Then
                    ProgressBarX_SPALM.Invoke(Sub() Me.ProgressBarX_SPALM.Value = 0)
                Else
                    Me.ProgressBarX_SPALM.Value = 0
                End If
            Catch ex2 As Exception
            End Try

        Catch ex As Exception
            AgregarLog(500, "Error desconocido en ExportarDataToHosting ALM: " & ex.Message)
        End Try

    End Sub

    Private Function GetQuery_TablasAlmacen() As String

        Dim tablas As String = ""
        tablas = "'tb_almacen_bitacora', " & _
                "'tb_almacen_bitacora_detalle', " & _
                "'tb_almacen_cierre', " & _
                "'tb_almacen_cierre_detalle', " & _
                "'tb_almacen_existencias', " & _
                "'tb_almacen_existencias_fisico', " & _
                "'tb_almacen_existencias_lote', " & _
                "'tb_almacen_existencias_marca', " & _
                "'tb_almacen_existencias_series', " & _
                "'tb_almacen_existencias_series2', " & _
                "'tb_almacen_existencias_series2_qr', " & _
                "'tb_almacen_inventario', " & _
                "'tb_almacen_inventario_detalle', " & _
                "'tb_almacen_inventario_no_contados', " & _
                "'tb_almacen_stocks', " & _
                "'tb_almacen_ubicacion', " & _
                "'tb_materiales', " & _
                "'tb_materiales_categoria', " & _
                "'tb_materiales_codigobarras', " & _
                "'tb_materiales_comisionistas', " & _
                "'tb_materiales_equivalencias', " & _
                "'tb_materiales_fotos', " & _
                "'tb_materiales_ftp', " & _
                "'tb_materiales_historialcostos', " & _
                "'tb_materiales_monedero', " & _
                "'tb_materiales_org', " & _
                "'tb_materiales_paquetes', " & _
                "'tb_materiales_precios', " & _
                "'tb_materiales_promocion', " & _
                "'tb_materiales_remate', " & _
                "'tb_materiales_resp', " & _
                "'tb_materiales_ubicacion', " & _
                "'tb_movimientos_material', " & _
                "'tb_notasalida', " & _
                "'tb_notasalida_detalle', " & _
                "'tb_notasalida_empaquetado', " & _
                "'tb_recibos', " & _
                "'tb_recibos_compdigitales', " & _
                "'tb_recibos_detalle', " & _
                "'tb_recibos_detalle_checkin', " & _
                "'tb_recibosmaterial' "

        Return tablas


    End Function

    Private Function Update_FotoSistema_Central(ByVal Campo As String, ByVal Tabla As String, _
                                        ByVal IdCampo As String, ByVal IdValor As String, _
                                        ByVal Imagen As Byte()) As Boolean

        Try
            Dim cmdtext As String
            cmdtext = "UPDATE " & Tabla & " set " & Campo & "=?File where " & IdCampo & " = '" & IdValor & "'"
            Dim cmd As New Global.MySqlConnector.MySqlCommand(cmdtext, cx_MySQL_Central)
            Try
                If cx_MySQL_Central.State = ConnectionState.Closed Then cx_MySQL_Central.Open()
                cmd.Parameters.AddWithValue("?File", Imagen)
                cmd.ExecuteNonQuery()
                cmd.Parameters.Clear()
            Catch ex As Exception
                AgregarLog(500, ex.Message & ", Error al actualizar imagen central: " & IdValor)
                'Me.lstLog.Items.Add(Calcula_FechaActual.ToString)
                'Me.lstLog.Items(lstLog.Items.Count - 1).SubItems.Add(ex.Message & ", Error al actualizar imagen central: " & IdValor)
            End Try

        Catch ex As Exception
            Return False
        End Try

        Return True

    End Function

    Private Function UsuarioNombre(ByVal CveMedico As String) As String

        Dim tb_medicos As DataTable
        tb_medicos = tb_Recordset_MySQL_local("Select ccvemedico, cnombre, cpriapellido, " & _
                                                   "csegapellido, cdscareaafectada, cgrado, cempleo, " & _
                                                   "cCedulaProf from cat_medico where ccvemedico = '" & CveMedico & "'")

        Dim usuario As String

        With tb_medicos
            Try
                usuario = .Rows(0).Item(1).ToString
                If usuario.Trim = "" Then
                    Return ""
                Else
                    usuario = .Rows(0).Item(6).ToString & " " & _
                                      .Rows(0).Item(1).ToString & " " & _
                                      .Rows(0).Item(2).ToString & " " & _
                                      .Rows(0).Item(3).ToString
                    Return usuario.Trim
                End If
            Catch ex As Exception
                Return ""
            End Try

        End With

        Return ""

    End Function

    Public Async Sub AgregarLog(ByVal tiempo As Integer, ByVal error_s As String)

        ' Introducir un retraso de 2 segundos (2000 milisegundos)
        Await System.Threading.Tasks.Task.Delay(tiempo)

        Try
            If lstLog.InvokeRequired Then
                lstLog.Invoke(Sub()
                                  Dim item = lstLog.Items.Add(Calcula_FechaActual().ToString())
                                  item.SubItems.Add(error_s)
                              End Sub)
            Else
                Dim item = Me.lstLog.Items.Add(Calcula_FechaActual().ToString())
                item.SubItems.Add(error_s)
            End If
            LogEventos.Escribir("AgregarLog. " & error_s)
        Catch ex2 As Exception
            LogEventos.Escribir("AgregarLog. " & ex2.Message)
        End Try

    End Sub

    'DETENER proceso

    Private Sub DetenerProcesoSP()

        If cts IsNot Nothing Then
            cts.Cancel()
        End If

    End Sub

    Private Sub IniciarProcesosSP()

        cts = New CancellationTokenSource()
        export_hosting_tarea = Task.Run(Sub() ExportarDataToHostingSP_Load(cts.Token), cts.Token)

    End Sub

    Private Sub ReiniciarProcesoSP()

        If export_hosting_tarea Is Nothing OrElse export_hosting_tarea.IsCompleted Then
            IniciarProcesosSP()
        End If

    End Sub

#End Region

#Region "MySQL"

    Public Function Insert_Admin(ByVal query As String) As Boolean

        Dim cmm_Comando As New Global.MySqlConnector.MySqlCommand(query, cx_MySQL_Admin)
        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_Admin.State = ConnectionState.Closed Then cx_MySQL_Admin.Open()
            cmm_Comando.ExecuteNonQuery()
        Catch ex As Exception
            If cx_MySQL_Admin.State = ConnectionState.Closed Then
                ciclo = ciclo + 1
                If ciclo = 3 Then
                    AgregarLog(500, ex.Message & ", Error al insertar registro admin: " & query)
                    Exit Try
                End If
                GoTo intenta_otravz
            Else
                AgregarLog(500, ex.Message & ", Error al insertar registro admin: " & query)
                Return False
            End If
            Return False
        End Try

        Return True

    End Function

    Public Function Insert_Admin(ByVal NomTabla As String, ByVal ValorSET As String) As Boolean

        Dim Cadena As String = ""
        Cadena = "Insert into " & NomTabla & _
                 " SET " & ValorSET

        Dim cmm_Comando As New Global.MySqlConnector.MySqlCommand(Cadena, cx_MySQL_Admin)
        Dim ciclo As Integer = 0

        Try

intenta_otravz:

            If cx_MySQL_Admin.State = ConnectionState.Closed Then cx_MySQL_Admin.Open()
            cmm_Comando.ExecuteNonQuery()
        Catch ex As Exception
            If cx_MySQL_Admin.State = ConnectionState.Closed Then
                ciclo = ciclo + 1
                If ciclo = 3 Then
                    AgregarLog(500, ex.Message & ", Error al insertar registro Admin: " & Cadena)
                    Exit Try
                End If
                GoTo intenta_otravz
            Else
                AgregarLog(500, ex.Message & ", Error al insertar registro Admin: " & Cadena)
                Return False
            End If
            Return False
        End Try

        Return True

    End Function

    Public Property cx_MySQL_Admin() As MySqlConnector.MySqlConnection
        Get
            Return cnn_MySQL_Admin
        End Get
        Set(ByVal Value As MySqlConnector.MySqlConnection)
            cnn_MySQL_Admin = Value
        End Set

    End Property

    Public Function tb_Recordset_MySQL_Admin(ByVal Comando As String) As Data.DataTable

        Dim da_Adaptador As New MySqlConnector.MySqlDataAdapter
        Dim cmm_Comando As New MySqlConnector.MySqlCommand(Comando, cx_MySQL_Admin)
        Dim dt_DataTable As New System.Data.DataTable
        da_Adaptador.SelectCommand = cmm_Comando
        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_Admin.State = ConnectionState.Closed Then cx_MySQL_Admin.Open()
            da_Adaptador.Fill(dt_DataTable)
        Catch ex As Exception
            If cx_MySQL_Admin.State = ConnectionState.Closed Then
                ciclo = ciclo + 1
                If ciclo = 3 Then
                    AgregarLog(500, ex.Message & ", Error al leer registro Admin: " & Comando)
                    Exit Try
                End If
                GoTo intenta_otravz
            Else
                AgregarLog(500, ex.Message & ", Error al leer registro Admin: " & Comando)
            End If
        End Try

        Return dt_DataTable


    End Function

    Public Function Update_Admin(ByVal Cadena As String) As Boolean

        Dim cmm_Comando As New Global.MySqlConnector.MySqlCommand(Cadena, cx_MySQL_Admin)
        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_Admin.State = ConnectionState.Closed Then cx_MySQL_Admin.Open()
            cmm_Comando.ExecuteNonQuery()
        Catch ex As Exception
            If cx_MySQL_Admin.State = ConnectionState.Closed Then
                ciclo = ciclo + 1
                If ciclo = 3 Then
                    AgregarLog(500, ex.Message & ", Error al actualizar registro Admin: " & Cadena)
                    Exit Try
                End If
                GoTo intenta_otravz
            Else
                AgregarLog(500, ex.Message & ", Error al actualizar registro Admin: " & Cadena)
                Return False
            End If
        End Try
        Return True

    End Function

    Public Function Update_Admin(ByVal NomTabla As String, ByVal ValorSET As String, ByVal CampoCond As String, ByVal ValorFiltro As String) As Boolean

        Dim Cadena As String = ""
        Cadena = "Update " & NomTabla & _
                 " SET " & ValorSET & _
                 " where " & CampoCond & _
                 " = '" & ValorFiltro & "'"

        Dim cmm_Comando As New Global.MySqlConnector.MySqlCommand(Cadena, cx_MySQL_Admin)
        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_Admin.State = ConnectionState.Closed Then cx_MySQL_Admin.Open()
            cmm_Comando.ExecuteNonQuery()
        Catch ex As Exception
            If cx_MySQL_Admin.State = ConnectionState.Closed Then
                ciclo = ciclo + 1
                If ciclo = 3 Then
                    AgregarLog(500, ex.Message & ", Error al actualizar registro Admin: " & Cadena)
                    'Me.lstLog.Items.Add(Calcula_FechaActual.ToString)
                    'Me.lstLog.Items(lstLog.Items.Count - 1).SubItems.Add(ex.Message & ", Error al actualizar registro Admin: " & Cadena)
                    Exit Try
                End If
                GoTo intenta_otravz
            Else
                AgregarLog(500, ex.Message & ", Error al actualizar registro Admin: " & Cadena)
                'Me.lstLog.Items.Add(Calcula_FechaActual.ToString)
                'Me.lstLog.Items(lstLog.Items.Count - 1).SubItems.Add(ex.Message & ", Error al actualizar registro Admin: " & Cadena)
                Return False
            End If
        End Try
        Return True

    End Function

    Public Function Test_MySQL_Admin(ByVal str_ConStr As String) As Boolean

        Dim ciclo As Integer = 0
        Try

intenta_otravz:

            If cx_MySQL_Admin.State = ConnectionState.Open Then Return True
            cx_MySQL_Admin.ConnectionString = str_ConStr
            cx_MySQL_Admin.Open()

            Dim cmd As New MySqlConnector.MySqlCommand("SET time_zone = 'America/Mexico_City';", cx_MySQL_CentralAsyncALM)
            cmd.ExecuteNonQuery()

            Return True
        Catch ex As Exception

            ciclo = ciclo + 1
            If ciclo = 3 Then
                Return False
            End If
            GoTo intenta_otravz
            Return False
        End Try

    End Function

    '****** Central Async ***********************************************

    Public Function Insert_CentralAsync(ByVal query As String) As Boolean

        Dim cmm_Comando As New Global.MySqlConnector.MySqlCommand(query, cx_MySQL_CentralAsync)
        Dim ciclo As Integer = 0
        Try

intenta_otravz:
            If cx_MySQL_CentralAsync.State = ConnectionState.Closed Then cx_MySQL_CentralAsync.Open()
            cmm_Comando.ExecuteNonQuery()
        Catch ex As Exception
            ciclo = ciclo + 1
            If ciclo = 3 Then
                AgregarLog(500, ex.Message & ", Error al insertar registro central: " & query)
                NotificarDesconexionCentral("Error al insertar en central: " & ex.Message)
                Exit Try
            End If
            GoTo intenta_otravz
        End Try

        Return True

    End Function

    Public Function Insert_CentralAsync(ByVal NomTabla As String, ByVal ValorSET As String) As Boolean

        Dim Cadena As String = ""
        Cadena = "Insert into " & NomTabla & _
                 " SET " & ValorSET

        Dim cmm_Comando As New Global.MySqlConnector.MySqlCommand(Cadena, cx_MySQL_CentralAsync)
        Dim ciclo As Integer = 0

        Try

intenta_otravz:
            If cx_MySQL_CentralAsync.State = ConnectionState.Closed Then cx_MySQL_CentralAsync.Open()
            cmm_Comando.ExecuteNonQuery()
        Catch ex As Exception
            ciclo = ciclo + 1
            If ciclo = 3 Then
                AgregarLog(500, ex.Message & ", Error al insertar registro central Async: " & Cadena)
                NotificarDesconexionCentral("Error al insertar registro en central: " & ex.Message)
                Exit Try
            End If
            GoTo intenta_otravz
        End Try

        Return True

    End Function

    Public Property cx_MySQL_CentralAsync() As MySqlConnector.MySqlConnection
        Get
            Return cnn_MySQL_CentralAsync
        End Get
        Set(ByVal Value As MySqlConnector.MySqlConnection)
            cnn_MySQL_CentralAsync = Value
        End Set

    End Property

    Public Function tb_Recordset_MySQL_CentralAsync(ByVal Comando As String) As Data.DataTable

        Dim da_Adaptador As New MySqlConnector.MySqlDataAdapter
        Dim cmm_Comando As New MySqlConnector.MySqlCommand(Comando, cx_MySQL_CentralAsync)
        Dim dt_DataTable As New System.Data.DataTable
        da_Adaptador.SelectCommand = cmm_Comando
        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_CentralAsync.State = ConnectionState.Closed Then cx_MySQL_CentralAsync.Open()
            da_Adaptador.Fill(dt_DataTable)
        Catch ex As Exception
            ciclo = ciclo + 1
            If ciclo = 3 Then
                AgregarLog(500, ex.Message & ", Error al leer registro central Async: " & Comando)
                NotificarDesconexionCentral("Error al leer de central: " & ex.Message)
                Exit Try
            End If
            GoTo intenta_otravz
        End Try

        Return dt_DataTable


    End Function

    Public Function Update_CentralAsync(ByVal Cadena As String) As Boolean

        Dim cmm_Comando As New Global.MySqlConnector.MySqlCommand(Cadena, cx_MySQL_CentralAsync)
        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_CentralAsync.State = ConnectionState.Closed Then cx_MySQL_CentralAsync.Open()
            cmm_Comando.ExecuteNonQuery()

        Catch ex As Exception
            ciclo = ciclo + 1
            If ciclo = 3 Then
                AgregarLog(500, ex.Message & ", Error al actualizar registro central Async: " & Cadena)
                NotificarDesconexionCentral("Error al actualizar central: " & ex.Message)
                Exit Try
            End If
            GoTo intenta_otravz
        End Try
        Return True

    End Function

    Public Function Update_CentralAsync(ByVal NomTabla As String, ByVal ValorSET As String, ByVal CampoCond As String, ByVal ValorFiltro As String) As Boolean

        Dim Cadena As String = ""
        Cadena = "Update " & NomTabla & _
                 " SET " & ValorSET & _
                 " where " & CampoCond & _
                 " = '" & ValorFiltro & "'"

        Dim cmm_Comando As New Global.MySqlConnector.MySqlCommand(Cadena, cx_MySQL_CentralAsync)
        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_CentralAsync.State = ConnectionState.Closed Then cx_MySQL_CentralAsync.Open()
            cmm_Comando.ExecuteNonQuery()
        Catch ex As Exception
            ciclo = ciclo + 1
            If ciclo = 3 Then
                AgregarLog(500, ex.Message & ", Error al actualizar registro central Async: " & Cadena)
                NotificarDesconexionCentral("Error al actualizar central: " & ex.Message)
                Exit Try
            End If
            GoTo intenta_otravz
        End Try
        Return True

    End Function

    Public Function Test_MySQL_CentralAsync(ByVal str_ConStr As String) As Boolean

        Dim ciclo As Integer = 0
        Try

intenta_otravz:

            If cx_MySQL_CentralAsync.State = ConnectionState.Open Then Return True
            cx_MySQL_CentralAsync.ConnectionString = str_ConStr
            cx_MySQL_CentralAsync.Open()

            Dim cmd As New MySqlConnector.MySqlCommand("SET time_zone = 'America/Mexico_City';", cx_MySQL_CentralAsync)
            cmd.ExecuteNonQuery()

            Return True
        Catch ex As Exception

            ciclo = ciclo + 1
            If ciclo = 3 Then
                Return False
            End If
            GoTo intenta_otravz
            Return False
        End Try

    End Function

    Private Function Update_FotoSistema_CentralAsync(ByVal Campo As String, ByVal Tabla As String, _
                                       ByVal IdCampo As String, ByVal IdValor As String, _
                                       ByVal Imagen As Byte()) As Boolean

        Try
            Dim cmdtext As String
            cmdtext = "UPDATE " & Tabla & " set " & Campo & "=?File where " & IdCampo & " = '" & IdValor & "'"
            Dim cmd As New Global.MySqlConnector.MySqlCommand(cmdtext, cx_MySQL_CentralAsync)
            Try
                If cx_MySQL_CentralAsync.State = ConnectionState.Closed Then cx_MySQL_CentralAsync.Open()
                cmd.Parameters.AddWithValue("?File", Imagen)
                cmd.ExecuteNonQuery()
                cmd.Parameters.Clear()
            Catch ex As Exception
                AgregarLog(500, ex.Message & ", Error al actualizar imagen central Async: " & IdValor)
            End Try

        Catch ex As Exception
            Return False
        End Try

        Return True

    End Function

    Public Function Delete_CentralAsync(ByVal Cadena As String) As Boolean

        Dim cmm_Comando As New Global.MySqlConnector.MySqlCommand(Cadena, cx_MySQL_CentralAsync)
        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_CentralAsync.State = ConnectionState.Closed Then cx_MySQL_CentralAsync.Open()
            cmm_Comando.ExecuteNonQuery()
        Catch ex As Exception

            ciclo = ciclo + 1
            If ciclo = 3 Then
                AgregarLog(500, ex.Message & ". Error al eliminar registro: " & Cadena)
                Exit Try
            End If
            GoTo intenta_otravz
        End Try
        Return True

    End Function


    '****** Central Async ALM ***********************************************

    Public Function Insert_CentralAsyncALM(ByVal query As String) As Boolean

        Dim cmm_Comando As New Global.MySqlConnector.MySqlCommand(query, cx_MySQL_CentralAsyncALM)
        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_CentralAsyncALM.State = ConnectionState.Closed Then cx_MySQL_CentralAsyncALM.Open()
            cmm_Comando.ExecuteNonQuery()
        Catch ex As Exception
            If cx_MySQL_CentralAsyncALM.State = ConnectionState.Closed Then
                ciclo = ciclo + 1
                If ciclo = 3 Then
                    AgregarLog(500, ex.Message & ", Error al insertar registro central ALM: " & query)
                    NotificarDesconexionCentral("Error al insertar en central ALM: " & ex.Message)
                    Exit Try
                End If
                GoTo intenta_otravz
            Else
                AgregarLog(500, ex.Message & ", Error al insertar registro central ALM: " & query)
                Return False
            End If
            Return False
        End Try

        Return True

    End Function

    Public Function Insert_CentralAsyncALM(ByVal NomTabla As String, ByVal ValorSET As String) As Boolean

        Dim Cadena As String = ""
        Cadena = "Insert into " & NomTabla & _
                 " SET " & ValorSET

        Dim cmm_Comando As New Global.MySqlConnector.MySqlCommand(Cadena, cx_MySQL_CentralAsyncALM)
        Dim ciclo As Integer = 0

        Try
intenta_otravz:
            If cx_MySQL_CentralAsyncALM.State = ConnectionState.Closed Then cx_MySQL_CentralAsyncALM.Open()
            cmm_Comando.ExecuteNonQuery()
        Catch ex As Exception
            If cx_MySQL_CentralAsyncALM.State = ConnectionState.Closed Then
                ciclo = ciclo + 1
                If ciclo = 3 Then
                    AgregarLog(500, ex.Message & ", Error al insertar registro central AsyncALM: " & Cadena)
                    NotificarDesconexionCentral("Error al insertar registro en central AsyncALM: " & ex.Message)
                    Exit Try
                End If
                GoTo intenta_otravz
            Else
                AgregarLog(500, ex.Message & ", Error al insertar registro central: " & Cadena)
                Return False
            End If
            Return False
        End Try

        Return True

    End Function

    Public Property cx_MySQL_CentralAsyncALM() As MySqlConnector.MySqlConnection
        Get
            Return cnn_MySQL_CentralAsyncALM
        End Get
        Set(ByVal Value As MySqlConnector.MySqlConnection)
            cnn_MySQL_CentralAsyncALM = Value
        End Set

    End Property

    Public Function tb_Recordset_MySQL_CentralAsyncALM(ByVal Comando As String) As Data.DataTable

        Dim da_Adaptador As New MySqlConnector.MySqlDataAdapter
        Dim cmm_Comando As New MySqlConnector.MySqlCommand(Comando, cx_MySQL_CentralAsyncALM)
        Dim dt_DataTable As New System.Data.DataTable
        da_Adaptador.SelectCommand = cmm_Comando
        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_CentralAsyncALM.State = ConnectionState.Closed Then cx_MySQL_CentralAsyncALM.Open()
            da_Adaptador.Fill(dt_DataTable)
        Catch ex As Exception
            If cx_MySQL_CentralAsyncALM.State = ConnectionState.Closed Then
                ciclo = ciclo + 1
                If ciclo = 3 Then
                    AgregarLog(500, ex.Message & ", Error al leer registro central AsyncALM: " & Comando)
                    NotificarDesconexionCentral("Error al leer de central AsyncALM: " & ex.Message)
                    Exit Try
                End If
                GoTo intenta_otravz
            Else
                AgregarLog(500, ex.Message & ", Error al leer registro central: " & Comando)
            End If
        End Try

        Return dt_DataTable


    End Function

    Public Function Update_CentralAsyncALM(ByVal Cadena As String) As Boolean

        Dim cmm_Comando As New Global.MySqlConnector.MySqlCommand(Cadena, cx_MySQL_CentralAsyncALM)
        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_CentralAsyncALM.State = ConnectionState.Closed Then cx_MySQL_CentralAsyncALM.Open()
            cmm_Comando.ExecuteNonQuery()

        Catch ex As Exception
            If cx_MySQL_CentralAsyncALM.State = ConnectionState.Closed Then
                ciclo = ciclo + 1
                If ciclo = 3 Then
                    AgregarLog(500, ex.Message & ", Error al actualizar registro central AsyncALM: " & Cadena)
                    NotificarDesconexionCentral("Error al actualizar central AsyncALM: " & ex.Message)
                    Exit Try
                End If
                GoTo intenta_otravz
            Else
                AgregarLog(500, ex.Message & ", Error al actualizar registro central AsyncALM: " & Cadena)
                Return False
            End If
        End Try
        Return True

    End Function

    Public Function Update_CentralAsyncALM(ByVal NomTabla As String, ByVal ValorSET As String, ByVal CampoCond As String, ByVal ValorFiltro As String) As Boolean

        Dim Cadena As String = ""
        Cadena = "Update " & NomTabla & _
                 " SET " & ValorSET & _
                 " where " & CampoCond & _
                 " = '" & ValorFiltro & "'"

        Dim cmm_Comando As New Global.MySqlConnector.MySqlCommand(Cadena, cx_MySQL_CentralAsyncALM)
        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_CentralAsyncALM.State = ConnectionState.Closed Then cx_MySQL_CentralAsyncALM.Open()
            cmm_Comando.ExecuteNonQuery()
        Catch ex As Exception
            If cx_MySQL_CentralAsyncALM.State = ConnectionState.Closed Then
                ciclo = ciclo + 1
                If ciclo = 3 Then
                    AgregarLog(500, ex.Message & ", Error al actualizar registro central AsyncALM: " & Cadena)
                    NotificarDesconexionCentral("Error al actualizar central AsyncALM: " & ex.Message)
                    Exit Try
                End If
                GoTo intenta_otravz
            Else
                AgregarLog(500, ex.Message & ", Error al actualizar registro central: " & Cadena)
                Return False
            End If
            Return False
        End Try
        Return True

    End Function

    Public Function Test_MySQL_CentralAsyncALM(ByVal str_ConStr As String) As Boolean

        Dim ciclo As Integer = 0
        Try

intenta_otravz:

            If cx_MySQL_CentralAsyncALM.State = ConnectionState.Open Then Return True
            cx_MySQL_CentralAsyncALM.ConnectionString = str_ConStr
            cx_MySQL_CentralAsyncALM.Open()

            Dim cmd As New MySqlConnector.MySqlCommand("SET time_zone = 'America/Mexico_City';", cx_MySQL_CentralAsyncALM)
            cmd.ExecuteNonQuery()

            Return True
        Catch ex As Exception

            ciclo = ciclo + 1
            If ciclo = 3 Then
                Return False
            End If
            GoTo intenta_otravz
            Return False
        End Try

    End Function

    Private Function Update_FotoSistema_CentralAsyncALM(ByVal Campo As String, ByVal Tabla As String, _
                                       ByVal IdCampo As String, ByVal IdValor As String, _
                                       ByVal Imagen As Byte()) As Boolean

        Try
            Dim cmdtext As String
            cmdtext = "UPDATE " & Tabla & " set " & Campo & "=?File where " & IdCampo & " = '" & IdValor & "'"
            Dim cmd As New Global.MySqlConnector.MySqlCommand(cmdtext, cx_MySQL_CentralAsyncALM)
            Try
                If cx_MySQL_CentralAsyncALM.State = ConnectionState.Closed Then cx_MySQL_CentralAsyncALM.Open()
                cmd.Parameters.AddWithValue("?File", Imagen)
                cmd.ExecuteNonQuery()
                cmd.Parameters.Clear()
            Catch ex As Exception
                AgregarLog(500, ex.Message & ", Error al actualizar imagen central Async: " & IdValor)
            End Try

        Catch ex As Exception
            Return False
        End Try

        Return True

    End Function

    Public Function Delete_CentralAsyncALM(ByVal Cadena As String) As Boolean

        Dim cmm_Comando As New Global.MySqlConnector.MySqlCommand(Cadena, cx_MySQL_CentralAsyncALM)
        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_CentralAsyncALM.State = ConnectionState.Closed Then cx_MySQL_CentralAsyncALM.Open()
            cmm_Comando.ExecuteNonQuery()
        Catch ex As Exception

            If cx_MySQL_CentralAsyncALM.State = ConnectionState.Closed Then
                ciclo = ciclo + 1
                If ciclo = 3 Then
                    AgregarLog(500, ex.Message & ". Error al eliminar registro: " & Cadena)
                    Exit Try
                End If
                GoTo intenta_otravz
            Else
                AgregarLog(500, ex.Message & ". Error al eliminar registro: " & Cadena)
                Return False
            End If
        End Try
        Return True

    End Function


    '*****************************************************

    Public Function Insert_Central(ByVal query As String) As Boolean

        Dim cmm_Comando As New Global.MySqlConnector.MySqlCommand(query, cx_MySQL_Central)
        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_Central.State = ConnectionState.Closed Then cx_MySQL_Central.Open()
            cmm_Comando.ExecuteNonQuery()
        Catch ex As Exception
            If cx_MySQL_Central.State = ConnectionState.Closed Then
                ciclo = ciclo + 1
                If ciclo = 3 Then
                    AgregarLog(500, ex.Message & ", Error al insertar registro central: " & query)
                    NotificarDesconexionCentral("Error al insertar en central: " & ex.Message)
                    Exit Try
                End If
                GoTo intenta_otravz
            Else
                AgregarLog(500, ex.Message & ", Error al insertar registro central: " & query)
                Return False
            End If
            Return False
        End Try

        Return True

    End Function

    Public Function Insert_Central(ByVal NomTabla As String, ByVal ValorSET As String) As Boolean

        Dim Cadena As String = ""
        Cadena = "Insert into " & NomTabla & _
                 " SET " & ValorSET

        Dim cmm_Comando As New Global.MySqlConnector.MySqlCommand(Cadena, cx_MySQL_Central)
        Dim ciclo As Integer = 0

        Try
intenta_otravz:
            If cx_MySQL_Central.State = ConnectionState.Closed Then cx_MySQL_Central.Open()
            cmm_Comando.ExecuteNonQuery()
        Catch ex As Exception
            If cx_MySQL_Central.State = ConnectionState.Closed Then
                ciclo = ciclo + 1
                If ciclo = 3 Then
                    AgregarLog(500, ex.Message & ", Error al insertar registro central: " & Cadena)
                    NotificarDesconexionCentral("Error al insertar registro en central: " & ex.Message)
                    Exit Try
                End If
                GoTo intenta_otravz
            Else
                AgregarLog(500, ex.Message & ", Error al insertar registro central: " & Cadena)
                Return False
            End If
            Return False
        End Try

        Return True

    End Function

    Public Property cx_MySQL_Central() As MySqlConnector.MySqlConnection
        Get
            Return cnn_MySQL_Central
        End Get
        Set(ByVal Value As MySqlConnector.MySqlConnection)
            cnn_MySQL_Central = Value
        End Set

    End Property

    Public Function tb_Recordset_MySQL_Central(ByVal Comando As String) As Data.DataTable

        Dim da_Adaptador As New MySqlConnector.MySqlDataAdapter
        Dim cmm_Comando As New MySqlConnector.MySqlCommand(Comando, cx_MySQL_Central)
        Dim dt_DataTable As New System.Data.DataTable
        da_Adaptador.SelectCommand = cmm_Comando
        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_Central.State = ConnectionState.Closed Then cx_MySQL_Central.Open()
            da_Adaptador.Fill(dt_DataTable)
        Catch ex As Exception
            If cx_MySQL_Central.State = ConnectionState.Closed Then
                ciclo = ciclo + 1
                If ciclo = 3 Then
                    AgregarLog(500, ex.Message & ", Error al leer registro central: " & Comando)
                    NotificarDesconexionCentral("Error al leer de central: " & ex.Message)
                    Exit Try
                End If
                GoTo intenta_otravz
            Else
                AgregarLog(500, ex.Message & ", Error al leer registro central: " & Comando)
            End If
        End Try

        Return dt_DataTable


    End Function

    Public Function Update_Central(ByVal Cadena As String) As Boolean

        Dim cmm_Comando As New Global.MySqlConnector.MySqlCommand(Cadena, cx_MySQL_Central)
        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_Central.State = ConnectionState.Closed Then cx_MySQL_Central.Open()
            cmm_Comando.ExecuteNonQuery()

        Catch ex As Exception
            If cx_MySQL_Central.State = ConnectionState.Closed Then
                ciclo = ciclo + 1
                If ciclo = 3 Then
                    AgregarLog(500, ex.Message & ", Error al actualizar registro central: " & Cadena)
                    NotificarDesconexionCentral("Error al actualizar central: " & ex.Message)
                    Exit Try
                End If
                GoTo intenta_otravz
            Else
                AgregarLog(500, ex.Message & ", Error al actualizar registro central: " & Cadena)
                Return False
            End If
        End Try
        Return True

    End Function

    Public Function Update_Central(ByVal NomTabla As String, ByVal ValorSET As String, ByVal CampoCond As String, ByVal ValorFiltro As String) As Boolean

        Dim Cadena As String = ""
        Cadena = "Update " & NomTabla & _
                 " SET " & ValorSET & _
                 " where " & CampoCond & _
                 " = '" & ValorFiltro & "'"

        Dim cmm_Comando As New Global.MySqlConnector.MySqlCommand(Cadena, cx_MySQL_Central)
        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_Central.State = ConnectionState.Closed Then cx_MySQL_Central.Open()
            cmm_Comando.ExecuteNonQuery()
        Catch ex As Exception
            If cx_MySQL_Central.State = ConnectionState.Closed Then
                ciclo = ciclo + 1
                If ciclo = 3 Then
                    AgregarLog(500, ex.Message & ", Error al actualizar registro central: " & Cadena)
                    NotificarDesconexionCentral("Error al actualizar central: " & ex.Message)
                    Exit Try
                End If
                GoTo intenta_otravz
            Else
                AgregarLog(500, ex.Message & ", Error al actualizar registro central: " & Cadena)
                Return False
            End If
        End Try
        Return True

    End Function

    Public Function Test_MySQL_Central(ByVal str_ConStr As String) As Boolean

        Dim ciclo As Integer = 0
        Try

intenta_otravz:

            If cx_MySQL_Central.State = ConnectionState.Open Then Return True
            cx_MySQL_Central.ConnectionString = str_ConStr
            cx_MySQL_Central.Open()

            Dim cmd As New MySqlConnector.MySqlCommand("SET time_zone = 'America/Mexico_City';", cx_MySQL_Central)
            cmd.ExecuteNonQuery()

            Return True
        Catch ex As Exception

            ciclo = ciclo + 1
            If ciclo = 3 Then
                Return False
            End If
            GoTo intenta_otravz
            Return False
        End Try

    End Function

    '*****************************************************

    Public Function Insert_local(ByVal NomTabla As String, ByVal ValorSET As String) As Boolean

        Dim Cadena As String = ""
        Cadena = "Insert into " & NomTabla & _
                 " SET " & ValorSET

        Dim cmm_Comando As New Global.MySqlConnector.MySqlCommand(Cadena, cx_MySQL_local)
        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_local.State = ConnectionState.Closed Then cx_MySQL_local.Open()
            cmm_Comando.ExecuteNonQuery()
        Catch ex As Exception
            If cx_MySQL_local.State = ConnectionState.Closed Then
                ciclo = ciclo + 1
                If ciclo = 3 Then
                    AgregarLog(500, ex.Message & ", Error al insertar registro local: " & Cadena)
                    Exit Try
                End If
                GoTo intenta_otravz
            Else
                AgregarLog(500, ex.Message & ", Error al insertar registro local: " & Cadena)
                Return False
            End If
            Return False
        End Try

        Return True

    End Function

    Public Property cx_MySQL_local() As MySqlConnector.MySqlConnection
        Get
            Return cnn_MySQL_Local
        End Get
        Set(ByVal Value As MySqlConnector.MySqlConnection)
            cnn_MySQL_Local = Value
        End Set

    End Property

    Public Function tb_Recordset_MySQL_local(ByVal Comando As String) As Data.DataTable

        Dim da_Adaptador As New MySqlConnector.MySqlDataAdapter
        Dim cmm_Comando As New MySqlConnector.MySqlCommand(Comando, cx_MySQL_local)
        Dim dt_DataTable As New System.Data.DataTable
        da_Adaptador.SelectCommand = cmm_Comando
        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_local.State = ConnectionState.Closed Then cx_MySQL_local.Open()
            da_Adaptador.Fill(dt_DataTable)
        Catch ex As Exception
            If cx_MySQL_local.State = ConnectionState.Closed Then
                ciclo = ciclo + 1
                If ciclo = 3 Then
                    AgregarLog(500, ex.Message & ", Error al leer registro local: " & Comando)
                    Exit Try
                End If
                GoTo intenta_otravz
            Else
                AgregarLog(500, ex.Message & ", Error al leer registro local: " & Comando)
            End If
        End Try

        Return dt_DataTable

    End Function

    Public Function Delete_Central(ByVal Cadena As String) As Boolean

        Dim cmm_Comando As New Global.MySqlConnector.MySqlCommand(Cadena, cx_MySQL_Central)
        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_Central.State = ConnectionState.Closed Then cx_MySQL_Central.Open()
            cmm_Comando.ExecuteNonQuery()
        Catch ex As Exception

            If cx_MySQL_Central.State = ConnectionState.Closed Then
                ciclo = ciclo + 1
                If ciclo = 3 Then
                    AgregarLog(500, ex.Message & ". Error al eliminar registro: " & Cadena)
                    Exit Try
                End If
                GoTo intenta_otravz
            Else
                AgregarLog(500, ex.Message & ". Error al eliminar registro: " & Cadena)
                Return False
            End If
        End Try
        Return True

    End Function

    Public Function Delete_local(ByVal Cadena As String) As Boolean

        Dim cmm_Comando As New Global.MySqlConnector.MySqlCommand(Cadena, cx_MySQL_local)
        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_local.State = ConnectionState.Closed Then cx_MySQL_local.Open()
            cmm_Comando.ExecuteNonQuery()
        Catch ex As Exception
            If cx_MySQL_local.State = ConnectionState.Closed Then
                ciclo = ciclo + 1
                If ciclo = 3 Then
                    AgregarLog(500, ex.Message & ". Error al eliminar registro: " & Cadena)
                    Exit Try
                End If
                GoTo intenta_otravz
            Else
                AgregarLog(500, ex.Message & ". Error al eliminar registro: " & Cadena)
                Return False
            End If
        End Try
        Return True

    End Function

    Public Function Update_local(ByVal NomTabla As String, ByVal ValorSET As String, ByVal CampoCond As String, ByVal ValorFiltro As String) As Boolean

        Dim Cadena As String = ""
        Cadena = "Update " & NomTabla & _
                 " SET " & ValorSET & _
                 " where " & CampoCond & _
                 " = '" & ValorFiltro & "'"

        Dim cmm_Comando As New Global.MySqlConnector.MySqlCommand(Cadena, cx_MySQL_local)
        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_local.State = ConnectionState.Closed Then cx_MySQL_local.Open()
            cmm_Comando.ExecuteNonQuery()
        Catch ex As Exception
            If cx_MySQL_local.State = ConnectionState.Closed Then
                ciclo = ciclo + 1
                If ciclo = 3 Then
                    AgregarLog(500, ex.Message & ", Error al actualizar registro local: " & Cadena)
                    Exit Try
                End If
                GoTo intenta_otravz
            Else
                AgregarLog(500, ex.Message & ", Error al actualizar registro local: " & Cadena)
                Return False
            End If
        End Try
        Return True

    End Function

    Public Function Update_local(ByVal Cadena As String) As Boolean

        Dim cmm_Comando As New Global.MySqlConnector.MySqlCommand(Cadena, cx_MySQL_local)
        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_local.State = ConnectionState.Closed Then cx_MySQL_local.Open()
            cmm_Comando.ExecuteNonQuery()
        Catch ex As Exception
            If cx_MySQL_local.State = ConnectionState.Closed Then
                ciclo = ciclo + 1
                If ciclo = 3 Then
                    AgregarLog(500, ex.Message & ", Error al actualizar registro local: " & Cadena)
                    Exit Try
                End If
                GoTo intenta_otravz
            Else
                AgregarLog(500, ex.Message & ", Error al actualizar registro local: " & Cadena)
                Return False
            End If

        End Try

        Return True

    End Function

    Public Function Update_Scripts_local(ByVal Cadena As String, ByRef error_s As String) As Boolean

        Dim cmm_Comando As New Global.MySqlConnector.MySqlCommand(Cadena, cx_MySQL_local)
        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_local.State = ConnectionState.Closed Then cx_MySQL_local.Open()
            cmm_Comando.ExecuteNonQuery()
        Catch ex As Exception
            If cx_MySQL_local.State = ConnectionState.Closed Then
                ciclo = ciclo + 1
                If ciclo = 3 Then
                    AgregarLog(500, ex.Message)
                    error_s = ex.Message
                    Exit Try
                End If
                GoTo intenta_otravz
            Else
                AgregarLog(500, ex.Message)
                error_s = ex.Message
                Return False
            End If

        End Try

        Return True

    End Function

    Public Function Test_MySQL_local(ByVal str_ConStr As String) As Boolean

        Dim ciclo As Integer = 0
        Try

intenta_otravz:

            If cx_MySQL_local.State = ConnectionState.Open Then Return True
            cx_MySQL_local.ConnectionString = str_ConStr
            cx_MySQL_local.Open()
            Return True
        Catch ex As Exception

            ciclo = ciclo + 1
            If ciclo = 3 Then
                Return False
            End If
            GoTo intenta_otravz
            Return False
        End Try

    End Function

    ' Local Async ****************************************

    Public Function Insert_localAsync(ByVal NomTabla As String, ByVal ValorSET As String) As Boolean

        Dim Cadena As String = ""
        Cadena = "Insert into " & NomTabla & _
                 " SET " & ValorSET

        Dim cmm_Comando As New Global.MySqlConnector.MySqlCommand(Cadena, cx_MySQL_localAsync)
        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_localAsync.State = ConnectionState.Closed Then cx_MySQL_localAsync.Open()
            cmm_Comando.ExecuteNonQuery()
        Catch ex As Exception
            If cx_MySQL_localAsync.State = ConnectionState.Closed Then
                ciclo = ciclo + 1
                If ciclo = 3 Then
                    AgregarLog(500, ex.Message & ", Error al insertar registro local Async: " & Cadena)
                    Exit Try
                End If
                GoTo intenta_otravz
            Else
                AgregarLog(500, ex.Message & ", Error al insertar registro local Async: " & Cadena)
                Return False
            End If
            Return False
        End Try

        Return True

    End Function

    Public Property cx_MySQL_localAsync() As MySqlConnector.MySqlConnection
        Get
            Return cnn_MySQL_LocalAsync
        End Get
        Set(ByVal Value As MySqlConnector.MySqlConnection)
            cnn_MySQL_LocalAsync = Value
        End Set

    End Property

    Public Function tb_Recordset_MySQL_localAsync(ByVal Comando As String) As Data.DataTable

        Dim da_Adaptador As New MySqlConnector.MySqlDataAdapter
        Dim cmm_Comando As New MySqlConnector.MySqlCommand(Comando, cx_MySQL_localAsync)
        Dim dt_DataTable As New System.Data.DataTable
        da_Adaptador.SelectCommand = cmm_Comando

        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_localAsync.State = ConnectionState.Closed Then cx_MySQL_localAsync.Open()
            da_Adaptador.Fill(dt_DataTable)

        Catch ex As Exception
            If cx_MySQL_localAsync.State = ConnectionState.Closed Then
                ciclo = ciclo + 1
                If ciclo = 3 Then
                    AgregarLog(500, ex.Message & ", Error al leer registro local Async: " & Comando)
                    Exit Try
                End If
                GoTo intenta_otravz
            Else
                AgregarLog(500, ex.Message & ", Error al leer registro local Async: " & Comando)
            End If
        End Try

        Return dt_DataTable

    End Function

    Public Function Delete_localAsync(ByVal Cadena As String) As Boolean

        Dim cmm_Comando As New MySqlConnector.MySqlCommand(Cadena, cx_MySQL_localAsync)
        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_localAsync.State = ConnectionState.Closed Then cx_MySQL_localAsync.Open()

            cmm_Comando.ExecuteNonQuery()
        Catch ex As Exception
            If cx_MySQL_localAsync.State = ConnectionState.Closed Then
                ciclo = ciclo + 1
                If ciclo = 3 Then
                    AgregarLog(500, ex.Message & ". Error al eliminar registro: " & Cadena)
                    Exit Try
                End If
                GoTo intenta_otravz
            Else
                AgregarLog(500, ex.Message & ". Error al eliminar registro: " & Cadena)
                Return False
            End If
        End Try

        Return True

    End Function

    Public Function Update_localAsync(ByVal NomTabla As String, ByVal ValorSET As String, ByVal CampoCond As String, ByVal ValorFiltro As String) As Boolean

        Dim Cadena As String = ""
        Cadena = "Update " & NomTabla & _
                 " SET " & ValorSET & _
                 " where " & CampoCond & _
                 " = '" & ValorFiltro & "'"

        Dim cmm_Comando As New Global.MySqlConnector.MySqlCommand(Cadena, cx_MySQL_localAsync)
        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_localAsync.State = ConnectionState.Closed Then cx_MySQL_localAsync.Open()
            cmm_Comando.ExecuteNonQuery()
        Catch ex As Exception
            If cx_MySQL_localAsync.State = ConnectionState.Closed Then
                ciclo = ciclo + 1
                If ciclo = 3 Then
                    AgregarLog(500, ex.Message & ", Error al actualizar registro local Async: " & Cadena)
                    Exit Try
                End If
                GoTo intenta_otravz
            Else
                AgregarLog(500, ex.Message & ", Error al actualizar registro local Async: " & Cadena)
                Return False
            End If
        End Try
        Return True

    End Function

    Public Function Update_localAsync(ByVal Cadena As String) As Boolean

        Dim cmm_Comando As New Global.MySqlConnector.MySqlCommand(Cadena, cx_MySQL_localAsync)
        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_localAsync.State = ConnectionState.Closed Then cx_MySQL_localAsync.Open()
            cmm_Comando.ExecuteNonQuery()
        Catch ex As Exception
            If cx_MySQL_localAsync.State = ConnectionState.Closed Then
                ciclo = ciclo + 1
                If ciclo = 3 Then
                    AgregarLog(500, ex.Message & ", Error al actualizar registro local Async: " & Cadena)
                    Exit Try
                End If
                GoTo intenta_otravz
            Else
                AgregarLog(500, ex.Message & ", Error al actualizar registro local Async: " & Cadena)
                Return False
            End If

        End Try

        Return True

    End Function

    Public Function Update_Scripts_localAsync(ByVal Cadena As String, ByRef error_s As String) As Boolean

        Dim cmm_Comando As New Global.MySqlConnector.MySqlCommand(Cadena, cx_MySQL_localAsync)
        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_localAsync.State = ConnectionState.Closed Then cx_MySQL_localAsync.Open()
            cmm_Comando.ExecuteNonQuery()
        Catch ex As Exception
            If cx_MySQL_localAsync.State = ConnectionState.Closed Then
                ciclo = ciclo + 1
                If ciclo = 3 Then
                    AgregarLog(500, ex.Message)
                    error_s = ex.Message
                    Exit Try
                End If
                GoTo intenta_otravz
            Else
                AgregarLog(500, ex.Message)
                error_s = ex.Message
                Return False
            End If

        End Try

        Return True

    End Function

    Public Function Test_MySQL_localAsync(ByVal str_ConStr As String) As Boolean

        Dim ciclo As Integer = 0
        Try

intenta_otravz:

            If cx_MySQL_localAsync.State = ConnectionState.Open Then Return True
            cx_MySQL_localAsync.ConnectionString = str_ConStr
            cx_MySQL_localAsync.Open()
            Return True
        Catch ex As Exception

            ciclo = ciclo + 1
            If ciclo = 3 Then
                Return False
            End If
            GoTo intenta_otravz
            Return False
        End Try

    End Function


    ' Local Async Almacen ****************************************

    Public Function Insert_localAsyncALM(ByVal NomTabla As String, ByVal ValorSET As String) As Boolean

        Dim Cadena As String = ""
        Cadena = "Insert into " & NomTabla & _
                 " SET " & ValorSET

        Dim cmm_Comando As New Global.MySqlConnector.MySqlCommand(Cadena, cx_MySQL_localAsyncALM)
        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_localAsyncALM.State = ConnectionState.Closed Then cx_MySQL_localAsyncALM.Open()
            cmm_Comando.ExecuteNonQuery()
        Catch ex As Exception
            If cx_MySQL_localAsyncALM.State = ConnectionState.Closed Then
                ciclo = ciclo + 1
                If ciclo = 3 Then
                    AgregarLog(500, ex.Message & ", Error al insertar registro local AsyncALM: " & Cadena)
                    Exit Try
                End If
                GoTo intenta_otravz
            Else
                AgregarLog(500, ex.Message & ", Error al insertar registro local AsyncALM: " & Cadena)
                Return False
            End If
            Return False
        End Try

        Return True

    End Function

    Public Property cx_MySQL_localAsyncALM() As MySqlConnector.MySqlConnection
        Get
            Return cnn_MySQL_LocalAsyncALM
        End Get
        Set(ByVal Value As MySqlConnector.MySqlConnection)
            cnn_MySQL_LocalAsyncALM = Value
        End Set

    End Property

    Public Function tb_Recordset_MySQL_localAsyncALM(ByVal Comando As String) As Data.DataTable

        Dim da_Adaptador As New MySqlConnector.MySqlDataAdapter
        Dim cmm_Comando As New MySqlConnector.MySqlCommand(Comando, cx_MySQL_localAsyncALM)
        Dim dt_DataTable As New System.Data.DataTable
        da_Adaptador.SelectCommand = cmm_Comando
        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_localAsyncALM.State = ConnectionState.Closed Then cx_MySQL_localAsyncALM.Open()
            da_Adaptador.Fill(dt_DataTable)
        Catch ex As Exception
            If cx_MySQL_localAsyncALM.State = ConnectionState.Closed Then
                ciclo = ciclo + 1
                If ciclo = 3 Then
                    AgregarLog(500, ex.Message & ", Error al leer registro local AsyncALM: " & Comando)
                    Exit Try
                End If
                GoTo intenta_otravz
            Else
                AgregarLog(500, ex.Message & ", Error al leer registro local AsyncALM: " & Comando)
            End If
        End Try

        Return dt_DataTable

    End Function

    Public Function Delete_localAsyncALM(ByVal Cadena As String) As Boolean

        Dim cmm_Comando As New Global.MySqlConnector.MySqlCommand(Cadena, cx_MySQL_localAsyncALM)
        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_localAsyncALM.State = ConnectionState.Closed Then cx_MySQL_localAsyncALM.Open()
            cmm_Comando.ExecuteNonQuery()
        Catch ex As Exception
            If cx_MySQL_localAsyncALM.State = ConnectionState.Closed Then
                ciclo = ciclo + 1
                If ciclo = 3 Then
                    AgregarLog(500, ex.Message & ". Error al eliminar registro: " & Cadena)
                    Exit Try
                End If
                GoTo intenta_otravz
            Else
                AgregarLog(500, ex.Message & ". Error al eliminar registro: " & Cadena)
                Return False
            End If
        End Try
        Return True

    End Function

    Public Function Update_localAsyncALM(ByVal NomTabla As String, ByVal ValorSET As String, ByVal CampoCond As String, ByVal ValorFiltro As String) As Boolean

        Dim Cadena As String = ""
        Cadena = "Update " & NomTabla & _
                 " SET " & ValorSET & _
                 " where " & CampoCond & _
                 " = '" & ValorFiltro & "'"

        Dim cmm_Comando As New MySqlConnector.MySqlCommand(Cadena, cx_MySQL_localAsyncALM)
        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_localAsyncALM.State = ConnectionState.Closed Then cx_MySQL_localAsyncALM.Open()
            cmm_Comando.ExecuteNonQuery()
        Catch ex As Exception
            If cx_MySQL_localAsyncALM.State = ConnectionState.Closed Then
                ciclo = ciclo + 1
                If ciclo = 3 Then
                    AgregarLog(500, ex.Message & ", Error al actualizar registro local AsyncALM: " & Cadena)
                    Exit Try
                End If
                GoTo intenta_otravz
            Else
                AgregarLog(500, ex.Message & ", Error al actualizar registro local AsyncALM: " & Cadena)
                Return False
            End If
        End Try
        Return True

    End Function

    Public Function Update_localAsyncALM(ByVal Cadena As String) As Boolean

        Dim cmm_Comando As New Global.MySqlConnector.MySqlCommand(Cadena, cx_MySQL_localAsyncALM)
        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_localAsyncALM.State = ConnectionState.Closed Then cx_MySQL_localAsyncALM.Open()
            cmm_Comando.ExecuteNonQuery()
        Catch ex As Exception
            If cx_MySQL_localAsyncALM.State = ConnectionState.Closed Then
                ciclo = ciclo + 1
                If ciclo = 3 Then
                    AgregarLog(500, ex.Message & ", Error al actualizar registro local AsyncALM: " & Cadena)
                    Exit Try
                End If
                GoTo intenta_otravz
            Else
                AgregarLog(500, ex.Message & ", Error al actualizar registro local AsyncALM: " & Cadena)
                Return False
            End If

        End Try

        Return True

    End Function

    Public Function Update_Scripts_localAsyncALM(ByVal Cadena As String, ByRef error_s As String) As Boolean

        Dim cmm_Comando As New Global.MySqlConnector.MySqlCommand(Cadena, cx_MySQL_localAsyncALM)
        Dim ciclo As Integer = 0
        Try
intenta_otravz:
            If cx_MySQL_localAsyncALM.State = ConnectionState.Closed Then cx_MySQL_localAsyncALM.Open()
            cmm_Comando.ExecuteNonQuery()
        Catch ex As Exception
            If cx_MySQL_localAsyncALM.State = ConnectionState.Closed Then
                ciclo = ciclo + 1
                If ciclo = 3 Then
                    AgregarLog(500, ex.Message)
                    error_s = ex.Message
                    Exit Try
                End If
                GoTo intenta_otravz
            Else
                AgregarLog(500, ex.Message)
                error_s = ex.Message
                Return False
            End If

        End Try

        Return True

    End Function

    Public Function Test_MySQL_localAsyncALM(ByVal str_ConStr As String) As Boolean

        Dim ciclo As Integer = 0
        Try

intenta_otravz:

            If cx_MySQL_localAsyncALM.State = ConnectionState.Open Then Return True
            cx_MySQL_localAsyncALM.ConnectionString = str_ConStr
            cx_MySQL_localAsyncALM.Open()
            Return True
        Catch ex As Exception

            ciclo = ciclo + 1
            If ciclo = 3 Then
                Return False
            End If
            GoTo intenta_otravz
            Return False
        End Try

    End Function


#End Region

#Region "Form"

    Private Sub frmInterface_Resize(sender As Object, e As EventArgs) Handles Me.Resize

        If Me.WindowState = FormWindowState.Minimized Then
            Me.ShowInTaskbar = False
            HistoMedicRPA.ShowBalloonTip(2000, "HistoMedic activo", "La aplicación sigue ejecutándose", ToolTipIcon.Info)
        End If

    End Sub

    Private Sub frmMonitor_FormClosing(ByVal sender As Object, ByVal e As System.Windows.Forms.FormClosingEventArgs) Handles Me.FormClosing

        Me.WindowState = FormWindowState.Minimized

        e.Cancel = True

    End Sub

    Private Sub frmMomitor_Load(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles MyBase.Load

        Me.Get_IpServidor()

        If Me.Conectar_Local() Then

            Dim tb_cat_consultorio As DataTable
            tb_cat_consultorio = tb_Recordset_MySQL_local("Select cClues, ftp_usuario, ftp_password, cSiglas from cat_consultorio")
            Me.CLUES = tb_cat_consultorio.Rows(0).Item("cClues").ToString
            Me.cSiglas = tb_cat_consultorio.Rows(0).Item("cSiglas").ToString

            Me.FTP_IP = "ftp://" & IpServidor & "/"
            Me.FTP_USUARIO = tb_cat_consultorio.Rows(0).Item("ftp_usuario").ToString
            Me.FTP_PASSWORD = tb_cat_consultorio.Rows(0).Item("ftp_password").ToString

            If Me.Conectar_Central(Me.CLUES) Then
                Me.Text = Me.Text & " " & Application.ProductVersion
                chkActivar.Checked = True

                'Proceso en segundo plano
                IniciarProcesosSP()

            Else
                Me.Text = Me.Text & " " & Application.ProductVersion
                chkActivar.Checked = True
                Me.NotificarDesconexionCentral("No fue posible conectar al servidor central al iniciar.")
            End If

        Else

            Application.ExitThread()

        End If

        load_init = False

    End Sub

    Private Sub btnConectarDBCentral_Click(sender As Object, e As EventArgs) Handles btnConectarDBCentral.Click

        If Not _servidorCentralConectado Then
            AgregarLog(100, "[Manual] Verificando e intentando reconexión al servidor central...")
            TimerReconexionCentral.Enabled = False
            TimerReconexionCentral.Interval = 1000 ' Iniciar reintento inmediato
            TimerReconexionCentral.Enabled = True
        Else
            AgregarLog(200, "[Central] La conexión al servidor central ya se encuentra activa y funcional.")
        End If

    End Sub

    Private Sub HistoclinMonitor_MouseDoubleClick(ByVal sender As System.Object, ByVal e As System.Windows.Forms.MouseEventArgs) Handles HistoMedicRPA.MouseDoubleClick

        Me.AbrirApp()

    End Sub

    Private Sub AbrirApp()

        Me.Show()
        Me.WindowState = FormWindowState.Normal
        Me.ShowInTaskbar = True
        Me.BringToFront()
        Me.Activate()
        Me.Refresh()

    End Sub

    Private Sub MenuAbrir_Click(sender As Object, e As EventArgs) Handles MenuAbrir.Click

        AbrirApp()

        'Me.HistoMedicRPA.Visible = False
        'Me.Bounds = Me.RestoreBounds
        'Me.Show()
        'Me.WindowState = FormWindowState.Normal

        ''Me.Show()
        ''Me.WindowState = FormWindowState.Normal

    End Sub

    Private Sub MenuCerrar_Click(sender As Object, e As EventArgs) Handles MenuCerrar.Click

        Me.TimerEnlace.Enabled = False
        Me.DetenerProcesoSP()
        Application.ExitThread()

    End Sub

    Private Sub TimerSTP_Tick(sender As Object, e As EventArgs) Handles TimerEnlace.Tick

        Me.IniciarRutina()

    End Sub

    Private Sub chkActivar_CheckedChanged(sender As Object, e As EventArgs) Handles chkActivar.CheckedChanged

        If Me.chkActivar.Checked Then
            Me.TimerEnlace.Enabled = True
            If Not load_init Then
                Me.ReiniciarProcesoSP()
            End If

        Else
            Me.TimerEnlace.Enabled = False
            Me.DetenerProcesoSP()
        End If

    End Sub

#End Region

#Region "Adjuntos"

    Private Sub ExportarAdjuntos()

        Try
            Dim tablas() As String = {
                "tb_pedidos_cliente_adjuntos",
                "tb_compras_cotizaciones_adjuntos",
                "tb_compras_cotizacion_interna_adjuntos",
                "tb_pedidos_proveedor_adjuntos",
                "tb_ventas_seguimiento",
                "tb_ventas_cotizacion_cliente_adjuntos",
                "tb_ventas_adjuntos"
            }

            Dim rawIp As String = Me.FTP_IP
            If String.IsNullOrWhiteSpace(rawIp) Then rawIp = IpServidor
            If String.IsNullOrWhiteSpace(rawIp) Then rawIp = "127.0.0.1"

            rawIp = rawIp.Replace("ftp://", "").Replace("ftps://", "").Replace("http://", "").Trim("/"c, " "c)
            If String.IsNullOrWhiteSpace(rawIp) Then rawIp = "127.0.0.1"

            Dim localFtpHost As String = "ftp://" & rawIp

            Dim localFtpUser As String = Me.FTP_USUARIO
            Dim localFtpPass As String = Me.FTP_PASSWORD

            Dim ftpLocalClient As New FtpClient(localFtpHost, localFtpUser, localFtpPass)

            For Each tabla As String In tablas
                Try
                    Dim query As String = "SELECT * FROM " & tabla & " WHERE sinc = 1"
                    Dim dt As DataTable = tb_Recordset_MySQL_local(query)

                    If dt IsNot Nothing AndAlso dt.Rows.Count > 0 Then
                        For Each row As DataRow In dt.Rows
                            Try
                                If Not IsDBNull(row("archivo")) AndAlso Not String.IsNullOrWhiteSpace(row("archivo").ToString()) Then
                                    Dim nombreArchivo As String = row("archivo").ToString().Trim()
                                    Dim rutaRemotaLocal As String = localFtpHost & "/TB_VENTAS/" & nombreArchivo

                                    Dim tempFolder As String = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HistoMedic_Temp")
                                    If Not System.IO.Directory.Exists(tempFolder) Then
                                        System.IO.Directory.CreateDirectory(tempFolder)
                                    End If
                                    Dim rutaTemporalLocal As String = System.IO.Path.Combine(tempFolder, nombreArchivo)

                                    ' 1. Descargar de FTP Local usando FtpClient.vb
                                    Dim descargado As Boolean = ftpLocalClient.DescargarArchivo(rutaRemotaLocal, rutaTemporalLocal)

                                    If descargado AndAlso System.IO.File.Exists(rutaTemporalLocal) Then
                                        ' 2. Subir al hosting de Hostgator
                                        Dim subido As Boolean = SubirArchivoHosting(rutaTemporalLocal, nombreArchivo, "/sistema.lfmcontrol.com.mx/Assets/files/ventas/")

                                        If subido Then
                                            ' 3. Actualizar sinc = 0 en la tabla local de origen
                                            Dim campoCond As String = "id"
                                            Dim valorCond As String = ""
                                            If dt.Columns.Contains("id") AndAlso Not IsDBNull(row("id")) Then
                                                valorCond = row("id").ToString()
                                            Else
                                                campoCond = "archivo"
                                                valorCond = nombreArchivo
                                            End If

                                            Update_local(tabla, "sinc = 0", campoCond, valorCond)
                                        End If

                                        ' Limpiar archivo temporal local
                                        Try
                                            If System.IO.File.Exists(rutaTemporalLocal) Then
                                                System.IO.File.Delete(rutaTemporalLocal)
                                            End If
                                        Catch exClean As Exception
                                        End Try
                                    End If
                                End If
                            Catch exRow As Exception
                                LogEventos.Escribir("Error al procesar registro en " & tabla & ": " & exRow.Message)
                            End Try
                        Next
                    End If
                Catch exTabla As Exception
                    LogEventos.Escribir("Error al consultar la tabla " & tabla & ": " & exTabla.Message)
                End Try
            Next
        Catch ex As Exception
            LogEventos.Escribir("Error general en ExportarAdjuntos: " & ex.Message)
        End Try

    End Sub

    Private Function SubirArchivoHosting(rutaLocal As String, nombreArchivoRemoto As String, FTP_CARPETA As String) As Boolean

        Dim ftp As New FTPHosting()
        Try
            Return ftp.SubirArchivo(rutaLocal, nombreArchivoRemoto, FTP_CARPETA)
        Catch ex As Exception
            LogEventos.Escribir("Error en SubirArchivoHosting: " & ex.Message)
        End Try

        Return False

    End Function

    Private Sub ExportarAdjuntosFotosMaterial()

        Try

            Dim rawIp As String = Me.FTP_IP
            If String.IsNullOrWhiteSpace(rawIp) Then rawIp = IpServidor
            If String.IsNullOrWhiteSpace(rawIp) Then rawIp = "127.0.0.1"

            rawIp = rawIp.Replace("ftp://", "").Replace("ftps://", "").Replace("http://", "").Trim("/"c, " "c)
            If String.IsNullOrWhiteSpace(rawIp) Then rawIp = "127.0.0.1"

            Dim localFtpHost As String = "ftp://" & rawIp

            Dim localFtpUser As String = Me.FTP_USUARIO
            Dim localFtpPass As String = Me.FTP_PASSWORD

            Dim ftpLocalClient As New FtpClient(localFtpHost, localFtpUser, localFtpPass)

            Try
                Dim query As String = "SELECT * FROM tb_materiales_ftp WHERE sinc = 1"
                Dim dt As DataTable = tb_Recordset_MySQL_local(query)

                If dt Is Nothing OrElse dt.Rows.Count = 0 Then Exit Try

                'Crear carpeta temporal una sola vez
                Dim tempFolder As String = Path.Combine(Path.GetTempPath(), "HistoMedic_Temp")

                If Not Directory.Exists(tempFolder) Then
                    Directory.CreateDirectory(tempFolder)
                End If

                For Each row As DataRow In dt.Rows

                    Try

                        Dim imagenesProcesadas As Integer = 0

                        For i As Integer = 1 To 5

                            Dim nombreArchivo As String = ""

                            If Not IsDBNull(row("img" & i)) Then
                                nombreArchivo = row("img" & i).ToString().Trim()
                            End If

                            'No existe imagen
                            If String.IsNullOrWhiteSpace(nombreArchivo) Then
                                imagenesProcesadas += 1
                                Continue For
                            End If

                            Dim rutaRemota As String = localFtpHost & "/TB_MATERIALES/" & nombreArchivo
                            Dim rutaTemporal As String = Path.Combine(tempFolder, nombreArchivo)

                            'Descargar desde FTP Local
                            If ftpLocalClient.DescargarArchivo(rutaRemota, rutaTemporal) Then

                                If File.Exists(rutaTemporal) Then

                                    'Subir al hosting
                                    If SubirArchivoHosting(rutaTemporal,
                                                           nombreArchivo,
                                                           "/sistema.lfmcontrol.com.mx/Assets/files/productos/") Then

                                        imagenesProcesadas += 1
                                    End If

                                    'Eliminar archivo temporal
                                    Try
                                        File.Delete(rutaTemporal)
                                    Catch
                                    End Try

                                End If

                            End If

                        Next

                        'Si las 5 imÃ¡genes fueron procesadas (existieran o no)
                        If imagenesProcesadas = 5 Then
                            Update_local("tb_materiales_ftp",
                                         "sinc = 0",
                                         "Id",
                                         row("Id").ToString())
                        End If

                    Catch exRow As Exception
                        LogEventos.Escribir("Error al procesar ID " &
                                            row("Id").ToString() &
                                            ": " &
                                            exRow.Message)
                    End Try

                Next

            Catch exTabla As Exception
                LogEventos.Escribir("Error al consultar tb_materiales_ftp: " & exTabla.Message)
            End Try
        Catch ex As Exception
            LogEventos.Escribir("Error general en ExportarAdjuntosFotosMaterial: " & ex.Message)
        End Try

    End Sub

    Private Sub ExportarAdjuntosAlmacen()

         Try
            Dim tablas() As String = {
                "tb_recibos_compdigitales"
            }

            Dim rawIp As String = Me.FTP_IP
            If String.IsNullOrWhiteSpace(rawIp) Then rawIp = IpServidor
            If String.IsNullOrWhiteSpace(rawIp) Then rawIp = "127.0.0.1"

            rawIp = rawIp.Replace("ftp://", "").Replace("ftps://", "").Replace("http://", "").Trim("/"c, " "c)
            If String.IsNullOrWhiteSpace(rawIp) Then rawIp = "127.0.0.1"

            Dim localFtpHost As String = "ftp://" & rawIp

            Dim localFtpUser As String = Me.FTP_USUARIO
            Dim localFtpPass As String = Me.FTP_PASSWORD

            Dim ftpLocalClient As New FtpClient(localFtpHost, localFtpUser, localFtpPass)

            For Each tabla As String In tablas
                Try
                    Dim query As String = "SELECT * FROM " & tabla & " WHERE sinc = 1"
                    Dim dt As DataTable = tb_Recordset_MySQL_local(query)

                    If dt IsNot Nothing AndAlso dt.Rows.Count > 0 Then
                        For Each row As DataRow In dt.Rows
                            Try
                                If Not IsDBNull(row("documento_ftp")) AndAlso Not String.IsNullOrWhiteSpace(row("documento_ftp").ToString()) Then
                                    Dim nombreArchivo As String = row("documento_ftp").ToString().Trim()
                                    Dim rutaRemotaLocal As String = localFtpHost & "/TB_RECIBOS/" & nombreArchivo

                                    Dim tempFolder As String = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HistoMedic_Temp")
                                    If Not System.IO.Directory.Exists(tempFolder) Then
                                        System.IO.Directory.CreateDirectory(tempFolder)
                                    End If
                                    Dim rutaTemporalLocal As String = System.IO.Path.Combine(tempFolder, nombreArchivo)

                                    ' 1. Descargar de FTP Local usando FtpClient.vb
                                    Dim descargado As Boolean = ftpLocalClient.DescargarArchivo(rutaRemotaLocal, rutaTemporalLocal)

                                    If descargado AndAlso System.IO.File.Exists(rutaTemporalLocal) Then
                                        ' 2. Subir al hosting de Hostgator
                                        Dim subido As Boolean = SubirArchivoHosting(rutaTemporalLocal, nombreArchivo, "/sistema.lfmcontrol.com.mx/Assets/files/ventas/")

                                        If subido Then
                                            ' 3. Actualizar sinc = 0 en la tabla local de origen
                                            Dim campoCond As String = "icvereciboscompdigitales"
                                            Dim valorCond As String = ""
                                            If dt.Columns.Contains("icvereciboscompdigitales") AndAlso Not IsDBNull(row("icvereciboscompdigitales")) Then
                                                valorCond = row("icvereciboscompdigitales").ToString()
                                            Else
                                                campoCond = "documento_ftp"
                                                valorCond = nombreArchivo
                                            End If

                                            Update_local(tabla, "sinc = 0", campoCond, valorCond)
                                        End If

                                        ' Limpiar archivo temporal local
                                        Try
                                            If System.IO.File.Exists(rutaTemporalLocal) Then
                                                System.IO.File.Delete(rutaTemporalLocal)
                                            End If
                                        Catch exClean As Exception
                                        End Try
                                    End If
                                End If
                            Catch exRow As Exception
                                LogEventos.Escribir("Error al procesar registro en " & tabla & ": " & exRow.Message)
                            End Try
                        Next
                    End If
                Catch exTabla As Exception
                    LogEventos.Escribir("Error al consultar la tabla " & tabla & ": " & exTabla.Message)
                End Try
            Next
        Catch ex As Exception
            LogEventos.Escribir("Error general en ExportarAdjuntos: " & ex.Message)
        End Try


    End Sub

#End Region

#Region "Notificaciones Cotizaciones Pendientes Compras"

    ''' <summary>
    ''' Rutina automática para notificar al personal de compras las partidas pendientes de cotizar de FLOWserve.
    ''' </summary>
    Public Sub NotificarCotizacionesPendientesFlowserve()
        Try
            ProcesarNotificacionCotizacionesPendientes(
                "FLOWserve",
                "frecuencia_notifica_flowserve",
                "correos_segcot_compras_flowserve",
                "fecha_ultima_notifica_flowserve",
                New Integer() {2, 3, 4, 6}
            )
        Catch ex As Exception
            AgregarLog(500, "Error en NotificarCotizacionesPendientesFlowserve: " & ex.Message)
            LogEventos.Escribir("Error en NotificarCotizacionesPendientesFlowserve: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Rutina automática para notificar al personal de compras las partidas pendientes de cotizar de DIVERSOS.
    ''' </summary>
    Public Sub NotificarCotizacionesPendientesDiversos()
        Try
            ProcesarNotificacionCotizacionesPendientes(
                "DIVERSOS",
                "frecuencia_notifica_diversos",
                "correos_segcot_compras_diversos",
                "fecha_ultima_notifica_diversos",
                New Integer() {5, 6}
            )
        Catch ex As Exception
            AgregarLog(500, "Error en NotificarCotizacionesPendientesDiversos: " & ex.Message)
            LogEventos.Escribir("Error en NotificarCotizacionesPendientesDiversos: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Lógica unificada para procesar notificaciones automáticas de compras.
    ''' Parametriza clasificación, frecuencia, destinatarios y columna de última fecha.
    ''' </summary>
    Private Sub ProcesarNotificacionCotizacionesPendientes(ByVal tipoNotificacion As String,
                                                          ByVal campoFrecuencia As String,
                                                          ByVal campoDestinatarios As String,
                                                          ByVal campoFechaUltima As String,
                                                          ByVal clasificacionesIds As Integer())
        If _procesandoNotificaciones Then Return

        _procesandoNotificaciones = True
        Try
            If cx_MySQL_local.State = ConnectionState.Closed Then
                Try
                    cx_MySQL_local.Open()
                Catch exConn As Exception
                    AgregarLog(500, String.Format("[{0}] No se pudo conectar a la BD local: {1}", tipoNotificacion, exConn.Message))
                    Return
                End Try
            End If

            ' 1. Obtener configuración de cat_consultorio
            Dim sqlConfig As String = String.Format("SELECT {0}, {1}, {2} FROM cat_consultorio LIMIT 1",
                                                    campoFrecuencia, campoDestinatarios, campoFechaUltima)
            Dim dtConfig As DataTable = tb_Recordset_MySQL_local(sqlConfig)
            If dtConfig Is Nothing OrElse dtConfig.Rows.Count = 0 Then
                AgregarLog(500, String.Format("[{0}] Configuración no encontrada en cat_consultorio.", tipoNotificacion))
                Return
            End If

            Dim rowConf As DataRow = dtConfig.Rows(0)

            ' Validar que la frecuencia sea mayor a 0
            Dim frecuenciaDias As Integer = 0
            If Not IsDBNull(rowConf(campoFrecuencia)) AndAlso IsNumeric(rowConf(campoFrecuencia)) Then
                frecuenciaDias = Convert.ToInt32(rowConf(campoFrecuencia))
            End If

            If frecuenciaDias <= 0 Then
                LogEventos.Escribir(String.Format("[{0}] Notificación omitida: frecuencia configurada ({1}) debe ser mayor a 0.", tipoNotificacion, frecuenciaDias))
                Return
            End If

            ' Validar que existan destinatarios
            Dim destinatarios As String = ""
            If Not IsDBNull(rowConf(campoDestinatarios)) Then
                destinatarios = rowConf(campoDestinatarios).ToString().Trim()
            End If

            If String.IsNullOrWhiteSpace(destinatarios) Then
                AgregarLog(500, String.Format("[{0}] Notificación omitida: no hay destinatarios en cat_consultorio.{1}.", tipoNotificacion, campoDestinatarios))
                Return
            End If

            ' Validar fecha de última notificación para respetar frecuencia y evitar duplicados
            Dim fechaUltimaNotif As Nullable(Of DateTime) = Nothing
            If Not IsDBNull(rowConf(campoFechaUltima)) Then
                Dim tmpFecha As DateTime
                If DateTime.TryParse(rowConf(campoFechaUltima).ToString(), tmpFecha) Then
                    fechaUltimaNotif = tmpFecha
                End If
            End If

            If fechaUltimaNotif.HasValue Then
                Dim diasTranscurridos As Integer = CInt(Math.Floor((DateTime.Now.Date - fechaUltimaNotif.Value.Date).TotalDays))
                If diasTranscurridos < frecuenciaDias Then
                    ' Aún no transcurren los días requeridos por la frecuencia
                    Return
                End If
            End If

            ' 2. Consultar partidas pendientes con SQL parametrizado
            Dim paramNames As New List(Of String)()
            Dim cmm As New MySqlConnector.MySqlCommand()
            cmm.Connection = cx_MySQL_local

            For i As Integer = 0 To clasificacionesIds.Length - 1
                Dim pName As String = "@clasif" & i
                paramNames.Add(pName)
                cmm.Parameters.AddWithValue(pName, clasificacionesIds(i))
            Next

            Dim sqlPartidas As String =
                "SELECT " & _
                "  cp.id AS clasificacion_id, " & _
                "  COALESCE(cp.clasificacion, 'SIN CLASIFICACIÓN') AS clasificacion_nombre, " & _
                "  c.id AS cotizacion_id, " & _
                "  COALESCE(c.folio_solicitud, '') AS folio_solicitud, " & _
                "  COALESCE(c.folio_cotizacion, '') AS folio_cotizacion, " & _
                "  c.fecha AS fecha_solicitud, " & _
                "  COALESCE(c.oportunidad_sistema_flowserve, '') AS oportunidad_flowserve, " & _
                "  v.id AS venta_id, " & _
                "  COALESCE(v.proyecto_id, '') AS proyecto_id, " & _
                "  COALESCE(v.titulo, '') AS proyecto_titulo, " & _
                "  COALESCE(v.cliente_final, '') AS cliente_final, " & _
                "  COALESCE(p.cDatGenRazonSocial, p.cDatGenNombreAbreviado, 'PROVEEDOR NO ASIGNADO') AS proveedor_nombre, " & _
                "  cd.id AS detalle_id, " & _
                "  COALESCE(cd.venta_detalle_id_partida, cd.id) AS partida_num, " & _
                "  COALESCE(cd.descripcion_proveedor, '') AS descripcion_proveedor, " & _
                "  COALESCE(cd.descripcion_adicional, '') AS descripcion_adicional, " & _
                "  COALESCE(cd.cantidad, 0) AS cantidad, " & _
                "  COALESCE(cd.ccveunidad, 'pza') AS unidad, " & _
                "  COALESCE(cd.codigo_proveedor, '') AS codigo_proveedor, " & _
                "  COALESCE(cd.num_parte, '') AS num_parte, " & _
                "  COALESCE(cd.ccvematerial, '') AS ccvematerial, " & _
                "  COALESCE(cd.tiempo_entrega, '') AS tiempo_entrega, " & _
                "  cd.precio_unitario " & _
                "FROM tb_compras_cotizaciones c " & _
                "INNER JOIN tb_compras_cotizaciones_detalle cd ON c.id = cd.cotizacion_id " & _
                "INNER JOIN tb_ventas v ON c.venta_id = v.id " & _
                "LEFT JOIN cat_clasificacion_proyectos cp ON v.clasificacion_proyecto_id = cp.id " & _
                "LEFT JOIN tb_proveedores p ON c.proveedor_id = p.icveProveedor " & _
                "WHERE c.enviado = 0 " & _
                "  AND (cd.precio_unitario = 0 OR cd.precio_unitario IS NULL) " & _
                "  AND v.clasificacion_proyecto_id IN (" & String.Join(",", paramNames) & ") " & _
                "ORDER BY cp.clasificacion, c.folio_solicitud, cd.id;"

            cmm.CommandText = sqlPartidas
            Dim dtPartidas As New DataTable()
            Dim da As New MySqlConnector.MySqlDataAdapter(cmm)
            da.Fill(dtPartidas)

            ' 3. Enviar únicamente si existen partidas pendientes
            If dtPartidas.Rows.Count = 0 Then
                LogEventos.Escribir(String.Format("[{0}] No existen partidas pendientes de cotizar. No se envía correo.", tipoNotificacion))
                Return
            End If

            AgregarLog(100, String.Format("[{0}] Se encontraron {1} partidas pendientes de cotizar. Generando correo HTML...", tipoNotificacion, dtPartidas.Rows.Count))

            ' 4. Generar HTML y Asunto
            Dim htmlCuerpo As String = GenerarHtmlCotizacionesPendientes(tipoNotificacion, dtPartidas, frecuenciaDias)
            Dim asunto As String = String.Format("[LFMControl] Cotizaciones Pendientes de Cotizar - {0} ({1} partidas)", tipoNotificacion, dtPartidas.Rows.Count)

            ' 5. Enviar correo a los destinatarios configurados
            Dim enviadoExitoso As Boolean = EnviarCorreoNotificacionHTML(destinatarios, asunto, htmlCuerpo)

            If enviadoExitoso Then
                ' 6. Actualizar fecha_ultima_notifica en cat_consultorio para evitar duplicados
                Dim sqlUpdate As String = String.Format("UPDATE cat_consultorio SET {0} = NOW()", campoFechaUltima)
                Using cmmUpd As New MySqlConnector.MySqlCommand(sqlUpdate, cx_MySQL_local)
                    If cx_MySQL_local.State = ConnectionState.Closed Then cx_MySQL_local.Open()
                    cmmUpd.ExecuteNonQuery()
                End Using

                AgregarLog(200, String.Format("[{0}] Notificación enviada con éxito a: {1} ({2} partidas notificadas).", tipoNotificacion, destinatarios, dtPartidas.Rows.Count))
            Else
                AgregarLog(500, String.Format("[{0}] Error al enviar correo a: {1}. Se reintentará en el próximo ciclo.", tipoNotificacion, destinatarios))
            End If

        Catch ex As Exception
            AgregarLog(500, String.Format("Error en ProcesarNotificacionCotizacionesPendientes ({0}): {1}", tipoNotificacion, ex.Message))
            LogEventos.Escribir(String.Format("Error en ProcesarNotificacionCotizacionesPendientes ({0}): {1} - Stack: {2}", tipoNotificacion, ex.Message, ex.StackTrace))
        Finally
            _procesandoNotificaciones = False
        End Try
    End Sub

    ''' <summary>
    ''' Construye el cuerpo del correo en HTML jerárquico:
    ''' Clasificación de Proyecto -> Solicitud de Cotización -> Partidas Pendientes
    ''' </summary>
    Private Function GenerarHtmlCotizacionesPendientes(ByVal tipoNotificacion As String, ByVal dt As DataTable, ByVal frecuenciaDias As Integer) As String
        Dim sb As New System.Text.StringBuilder()

        ' Agrupar datos por clasificación
        Dim clasificaciones As New List(Of String)()
        For Each r As DataRow In dt.Rows
            Dim cName As String = If(Not IsDBNull(r("clasificacion_nombre")), r("clasificacion_nombre").ToString().Trim(), "SIN CLASIFICACIÓN")
            If Not clasificaciones.Contains(cName) Then
                clasificaciones.Add(cName)
            End If
        Next

        ' Conteo de solicitudes únicas
        Dim totalSolicitudes As New HashSet(Of String)()
        For Each r As DataRow In dt.Rows
            Dim keySol As String = If(Not IsDBNull(r("cotizacion_id")), r("cotizacion_id").ToString(), "")
            totalSolicitudes.Add(keySol)
        Next

        sb.AppendLine("<!DOCTYPE html>")
        sb.AppendLine("<html>")
        sb.AppendLine("<head>")
        sb.AppendLine("<meta http-equiv=""Content-Type"" content=""text/html; charset=utf-8"" />")
        sb.AppendLine("<style type=""text/css"">")
        sb.AppendLine("  body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f1f5f9; margin: 0; padding: 20px; color: #1e293b; }")
        sb.AppendLine("  .container { max-width: 900px; margin: 0 auto; background-color: #ffffff; border: 1px solid #cbd5e1; border-radius: 8px; overflow: hidden; box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1); }")
        sb.AppendLine("  .header { background: linear-gradient(135deg, #0f172a 0%, #1e3a8a 100%); color: #ffffff; padding: 24px 30px; text-align: left; }")
        sb.AppendLine("  .header h1 { margin: 0 0 6px 0; font-size: 20px; font-weight: 700; letter-spacing: -0.5px; }")
        sb.AppendLine("  .header p { margin: 0; font-size: 13px; color: #cbd5e1; }")
        sb.AppendLine("  .stats-bar { display: table; width: 100%; background-color: #f8fafc; border-bottom: 1px solid #e2e8f0; padding: 12px 30px; box-sizing: border-box; }")
        sb.AppendLine("  .stat-item { display: table-cell; vertical-align: middle; font-size: 12px; color: #475569; }")
        sb.AppendLine("  .stat-badge { display: inline-block; background-color: #1e3a8a; color: #ffffff; font-weight: bold; border-radius: 12px; padding: 2px 8px; font-size: 11px; margin-left: 4px; }")
        sb.AppendLine("  .content { padding: 25px 30px; }")
        sb.AppendLine("  .clasif-section { margin-bottom: 30px; }")
        sb.AppendLine("  .clasif-title { background: #e0e7ff; color: #1e1b4b; font-size: 15px; font-weight: 700; padding: 10px 16px; border-left: 5px solid #2563eb; border-radius: 4px; margin-bottom: 16px; text-transform: uppercase; }")
        sb.AppendLine("  .solicitud-card { background: #ffffff; border: 1px solid #e2e8f0; border-radius: 6px; margin-bottom: 20px; overflow: hidden; }")
        sb.AppendLine("  .solicitud-header { background-color: #f8fafc; border-bottom: 1px solid #e2e8f0; padding: 12px 16px; }")
        sb.AppendLine("  .sol-title { font-size: 14px; font-weight: 700; color: #0f172a; margin-bottom: 4px; }")
        sb.AppendLine("  .sol-meta { font-size: 12px; color: #475569; line-height: 1.5; }")
        sb.AppendLine("  .sol-meta strong { color: #1e293b; }")
        sb.AppendLine("  table.items-table { width: 100%; border-collapse: collapse; font-size: 12px; text-align: left; }")
        sb.AppendLine("  table.items-table th { background-color: #f1f5f9; color: #334155; font-weight: 600; padding: 9px 12px; border-bottom: 2px solid #cbd5e1; font-size: 11px; text-transform: uppercase; }")
        sb.AppendLine("  table.items-table td { padding: 9px 12px; border-bottom: 1px solid #e2e8f0; vertical-align: top; }")
        sb.AppendLine("  table.items-table tr:nth-child(even) { background-color: #f8fafc; }")
        sb.AppendLine("  .tag-code { display: inline-block; background-color: #f1f5f9; border: 1px solid #cbd5e1; border-radius: 3px; padding: 1px 5px; font-family: Consolas, monospace; font-size: 11px; color: #0f172a; }")
        sb.AppendLine("  .desc-adic { font-size: 11px; color: #64748b; margin-top: 3px; font-style: italic; }")
        sb.AppendLine("  .footer { background-color: #f8fafc; border-top: 1px solid #e2e8f0; padding: 18px 30px; font-size: 11px; color: #64748b; text-align: center; }")
        sb.AppendLine("</style>")
        sb.AppendLine("</head>")
        sb.AppendLine("<body>")
        sb.AppendLine("<div class=""container"">")

        ' Encabezado principal
        sb.AppendLine("  <div class=""header"">")
        sb.AppendLine(String.Format("    <h1>Notificación de Cotizaciones Pendientes - {0}</h1>", System.Net.WebUtility.HtmlEncode(tipoNotificacion)))
        sb.AppendLine(String.Format("    <p>Partidas pendientes de cotizar registradas en solicitudes a proveedores &bull; Generado el {0}</p>", DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss")))
        sb.AppendLine("  </div>")

        ' Barra de estadísticas
        sb.AppendLine("  <div class=""stats-bar"">")
        sb.AppendLine(String.Format("    <div class=""stat-item"">Solicitudes con pendientes: <span class=""stat-badge"">{0}</span></div>", totalSolicitudes.Count))
        sb.AppendLine(String.Format("    <div class=""stat-item"">Total Partidas Pendientes: <span class=""stat-badge"">{0}</span></div>", dt.Rows.Count))
        sb.AppendLine(String.Format("    <div class=""stat-item"" style=""text-align: right;"">Frecuencia programada: <strong>Cada {0} día(s)</strong></div>", frecuenciaDias))
        sb.AppendLine("  </div>")

        sb.AppendLine("  <div class=""content"">")

        ' Nivel 1: Clasificación de Proyecto
        For Each clasif In clasificaciones
            Dim clasifCurrent As String = clasif
            Dim rowsClasif As DataRow() = dt.Select(String.Format("clasificacion_nombre = '{0}'", clasifCurrent.Replace("'", "''")))

            sb.AppendLine("    <div class=""clasif-section"">")
            sb.AppendLine(String.Format("      <div class=""clasif-title"">&#9658; Clasificación: {0} ({1} partidas)</div>", System.Net.WebUtility.HtmlEncode(clasifCurrent), rowsClasif.Length))

            ' Nivel 2: Solicitudes de Cotización dentro de la clasificación
            Dim cotizacionIds As New List(Of String)()
            For Each r In rowsClasif
                Dim cotIdStr As String = r("cotizacion_id").ToString()
                If Not cotizacionIds.Contains(cotIdStr) Then
                    cotizacionIds.Add(cotIdStr)
                End If
            Next

            For Each cotIdStr In cotizacionIds
                Dim idCurrent As String = cotIdStr
                Dim rowsCot As DataRow() = dt.Select(String.Format("clasificacion_nombre = '{0}' AND cotizacion_id = {1}", clasifCurrent.Replace("'", "''"), idCurrent))
                If rowsCot.Length = 0 Then Continue For

                Dim primerRow As DataRow = rowsCot(0)
                Dim folioSol As String = If(Not IsDBNull(primerRow("folio_solicitud")) AndAlso Not String.IsNullOrWhiteSpace(primerRow("folio_solicitud").ToString()), primerRow("folio_solicitud").ToString().Trim(), "ID #" & idCurrent)
                Dim folioCot As String = If(Not IsDBNull(primerRow("folio_cotizacion")), primerRow("folio_cotizacion").ToString().Trim(), "")
                Dim fchSolStr As String = If(Not IsDBNull(primerRow("fecha_solicitud")), Format(primerRow("fecha_solicitud"), "dd/MM/yyyy"), "-")
                Dim pryId As String = If(Not IsDBNull(primerRow("proyecto_id")), primerRow("proyecto_id").ToString().Trim(), "")
                Dim pryTit As String = If(Not IsDBNull(primerRow("proyecto_titulo")), primerRow("proyecto_titulo").ToString().Trim(), "")
                Dim provNom As String = If(Not IsDBNull(primerRow("proveedor_nombre")), primerRow("proveedor_nombre").ToString().Trim(), "PROVEEDOR NO ASIGNADO")
                Dim oportFlow As String = If(Not IsDBNull(primerRow("oportunidad_flowserve")), primerRow("oportunidad_flowserve").ToString().Trim(), "")
                Dim clieFinal As String = If(Not IsDBNull(primerRow("cliente_final")), primerRow("cliente_final").ToString().Trim(), "")

                sb.AppendLine("      <div class=""solicitud-card"">")
                sb.AppendLine("        <div class=""solicitud-header"">")
                sb.AppendLine(String.Format("          <div class=""sol-title"">Solicitud: {0}{1} &bull; Proveedor: {2}</div>",
                                            System.Net.WebUtility.HtmlEncode(folioSol),
                                            If(Not String.IsNullOrWhiteSpace(folioCot), " (Cotiz: " & System.Net.WebUtility.HtmlEncode(folioCot) & ")", ""),
                                            System.Net.WebUtility.HtmlEncode(provNom)))

                sb.AppendLine("          <div class=""sol-meta"">")
                sb.AppendLine(String.Format("            <strong>Proyecto:</strong> {0} - {1} &bull; <strong>Fecha Solicitud:</strong> {2}",
                                            System.Net.WebUtility.HtmlEncode(pryId),
                                            System.Net.WebUtility.HtmlEncode(pryTit),
                                            System.Net.WebUtility.HtmlEncode(fchSolStr)))

                If Not String.IsNullOrWhiteSpace(clieFinal) Then
                    sb.AppendLine(String.Format(" &bull; <strong>Cliente Final:</strong> {0}", System.Net.WebUtility.HtmlEncode(clieFinal)))
                End If
                If Not String.IsNullOrWhiteSpace(oportFlow) Then
                    sb.AppendLine(String.Format(" &bull; <strong>Oportunidad Flowserve:</strong> {0}", System.Net.WebUtility.HtmlEncode(oportFlow)))
                End If

                sb.AppendLine("          </div>")
                sb.AppendLine("        </div>")

                ' Nivel 3: Tabla de Partidas Pendientes
                sb.AppendLine("        <table class=""items-table"">")
                sb.AppendLine("          <thead>")
                sb.AppendLine("            <tr>")
                sb.AppendLine("              <th style=""width: 5%; text-align: center;"">#</th>")
                sb.AppendLine("              <th style=""width: 12%; text-align: center;"">Cantidad</th>")
                sb.AppendLine("              <th style=""width: 13%;"">Cód. Prov.</th>")
                sb.AppendLine("              <th style=""width: 15%;"">No. Parte</th>")
                sb.AppendLine("              <th style=""width: 43%;"">Descripción / Concepto</th>")
                sb.AppendLine("              <th style=""width: 12%; text-align: center;"">T. Entrega</th>")
                sb.AppendLine("            </tr>")
                sb.AppendLine("          </thead>")
                sb.AppendLine("          <tbody>")

                For Each r In rowsCot
                    Dim partidaNum As String = If(Not IsDBNull(r("partida_num")), r("partida_num").ToString(), "-")
                    Dim cantVal As Double = If(Not IsDBNull(r("cantidad")), Convert.ToDouble(r("cantidad")), 0)
                    Dim unidadStr As String = If(Not IsDBNull(r("unidad")), r("unidad").ToString().Trim(), "pza")
                    Dim codProv As String = If(Not IsDBNull(r("codigo_proveedor")), r("codigo_proveedor").ToString().Trim(), "")
                    Dim numParte As String = If(Not IsDBNull(r("num_parte")), r("num_parte").ToString().Trim(), "")
                    Dim descProv As String = If(Not IsDBNull(r("descripcion_proveedor")), r("descripcion_proveedor").ToString().Trim(), "")
                    Dim descAdic As String = If(Not IsDBNull(r("descripcion_adicional")), r("descripcion_adicional").ToString().Trim(), "")
                    Dim tiempoEnt As String = If(Not IsDBNull(r("tiempo_entrega")), r("tiempo_entrega").ToString().Trim(), "")

                    sb.AppendLine("            <tr>")
                    sb.AppendLine(String.Format("              <td style=""text-align: center; font-weight: bold; color: #475569;"">{0}</td>", System.Net.WebUtility.HtmlEncode(partidaNum)))
                    sb.AppendLine(String.Format("              <td style=""text-align: center; font-weight: bold;"">{0:N2} {1}</td>", cantVal, System.Net.WebUtility.HtmlEncode(unidadStr)))
                    sb.AppendLine(String.Format("              <td>{0}</td>", If(Not String.IsNullOrWhiteSpace(codProv), "<span class=""tag-code"">" & System.Net.WebUtility.HtmlEncode(codProv) & "</span>", "-")))
                    sb.AppendLine(String.Format("              <td>{0}</td>", If(Not String.IsNullOrWhiteSpace(numParte), "<span class=""tag-code"">" & System.Net.WebUtility.HtmlEncode(numParte) & "</span>", "-")))

                    sb.Append("              <td>")
                    sb.Append(System.Net.WebUtility.HtmlEncode(descProv))
                    If Not String.IsNullOrWhiteSpace(descAdic) AndAlso Not descAdic.Equals(descProv, StringComparison.OrdinalIgnoreCase) Then
                        sb.Append(String.Format("<div class=""desc-adic"">{0}</div>", System.Net.WebUtility.HtmlEncode(descAdic)))
                    End If
                    sb.AppendLine("</td>")

                    sb.AppendLine(String.Format("              <td style=""text-align: center; color: #64748b;"">{0}</td>", If(Not String.IsNullOrWhiteSpace(tiempoEnt), System.Net.WebUtility.HtmlEncode(tiempoEnt), "-")))
                    sb.AppendLine("            </tr>")
                Next

                sb.AppendLine("          </tbody>")
                sb.AppendLine("        </table>")
                sb.AppendLine("      </div>")
            Next

            sb.AppendLine("    </div>")
        Next

        sb.AppendLine("  </div>")

        ' Pie de página institucional
        sb.AppendLine("  <div class=""footer"">")
        sb.AppendLine("    <p style=""margin: 0 0 4px 0; font-weight: 600;"">HistoMedic LFM RPA Robot &bull; Notificación Automática de Cotizaciones</p>")
        sb.AppendLine("    <p style=""margin: 0;"">Este mensaje fue generado automáticamente según la frecuencia programada en cat_consultorio. Por favor no responder a este correo.</p>")
        sb.AppendLine("  </div>")
        sb.AppendLine("</div>")
        sb.AppendLine("</body>")
        sb.AppendLine("</html>")

        Return sb.ToString()
    End Function

    ''' <summary>
    ''' Envía un correo electrónico en formato HTML a múltiples destinatarios usando Chilkat MailMan.
    ''' No muestra cuadros de diálogo interactivos (MsgBox) y registra cualquier error en logs.
    ''' </summary>
    Private Function EnviarCorreoNotificacionHTML(ByVal destinatarios As String, ByVal asunto As String, ByVal cuerpoHtml As String) As Boolean
        Try
            Dim mailman As New Chilkat.MailMan()
            Dim success As Boolean = mailman.UnlockComponent("MAIL87654321_3C7B9122j163")
            If Not success Then
                LogEventos.Escribir("Error al desbloquear Chilkat MailMan: " & mailman.LastErrorText)
                Return False
            End If

            Dim dtSmtp As DataTable = tb_Recordset_MySQL_local("SELECT cpuerto, cdireccion, cclave, chost, cremitente, ccorreoremitente FROM cat_confserversmtp WHERE iActivo = 1 LIMIT 1")
            If dtSmtp Is Nothing OrElse dtSmtp.Rows.Count = 0 Then
                LogEventos.Escribir("Error: No se encontró servidor SMTP activo en cat_confserversmtp (iActivo = 1).")
                Return False
            End If

            Dim rowSmtp As DataRow = dtSmtp.Rows(0)
            Dim puertoSmtp As Integer = 25
            If Not IsDBNull(rowSmtp("cpuerto")) AndAlso IsNumeric(rowSmtp("cpuerto")) Then
                puertoSmtp = Convert.ToInt32(rowSmtp("cpuerto"))
            End If

            mailman.SmtpHost = rowSmtp("chost").ToString().Trim()
            mailman.SmtpPort = puertoSmtp
            mailman.SmtpUsername = rowSmtp("cdireccion").ToString().Trim()
            mailman.SmtpPassword = rowSmtp("cclave").ToString().Trim()
            mailman.SmtpSsl = False
            mailman.ReadTimeout = 30
            mailman.ConnectTimeout = 15

            Dim email As New Chilkat.Email()
            email.Subject = asunto
            email.AddHtmlAlternativeBody(cuerpoHtml)

            Dim remitenteNombre As String = If(Not IsDBNull(rowSmtp("cremitente")) AndAlso Not String.IsNullOrWhiteSpace(rowSmtp("cremitente").ToString()), rowSmtp("cremitente").ToString().Trim(), "LFM Control Robot")
            Dim remitenteCorreo As String = rowSmtp("cdireccion").ToString().Trim()
            email.FromName = remitenteNombre
            email.FromAddress = remitenteCorreo

            Dim separadores As Char() = New Char() {","c, ";"c}
            Dim listaCorreos As String() = destinatarios.Split(separadores, StringSplitOptions.RemoveEmptyEntries)
            Dim totalAgregados As Integer = 0

            For Each correoRaw As String In listaCorreos
                Dim correoLimpio As String = correoRaw.Trim()
                If Not String.IsNullOrWhiteSpace(correoLimpio) AndAlso EsDireccionCorreoValida(correoLimpio) Then
                    email.AddTo("", correoLimpio)
                    totalAgregados += 1
                End If
            Next

            If totalAgregados = 0 Then
                LogEventos.Escribir("Error: No se encontraron destinatarios con formato de correo válido en: " & destinatarios)
                Return False
            End If

            success = mailman.SendEmail(email)
            If Not success Then
                LogEventos.Escribir("Fallo de Chilkat al enviar correo: " & mailman.LastErrorText)
                mailman.CloseSmtpConnection()
                Return False
            End If

            mailman.CloseSmtpConnection()
            Return True

        Catch ex As Exception
            LogEventos.Escribir("Excepción en EnviarCorreoNotificacionHTML: " & ex.Message)
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Valida sintácticamente una dirección de correo electrónico usando System.Net.Mail.MailAddress.
    ''' </summary>
    Private Function EsDireccionCorreoValida(ByVal emailStr As String) As Boolean
        Try
            If String.IsNullOrWhiteSpace(emailStr) Then Return False
            Dim addr As New System.Net.Mail.MailAddress(emailStr)
            Return True
        Catch ex As Exception
            Return False
        End Try
    End Function

#End Region

End Class


Public Class LogEventos

    Private Shared ReadOnly RutaLog As String =
        Path.Combine(Application.StartupPath, "Eventos.log")

    Private Const LimiteMB As Long = 100

    Public Shared Sub Escribir(Mensaje As String)

        Try

            ' Verificar tamaño del log
            If File.Exists(RutaLog) Then

                Dim info As New FileInfo(RutaLog)
                Dim tamanoBytes As Long = info.Length

                If tamanoBytes >= (1 * 1024 * 1024) Then
                    '$"Eventos_{DateTime.Now:yyyyMMdd_HHmmss}.log")
                    Dim respaldo As String =
                        Path.Combine(
                            Application.StartupPath,
                            String.Format(
                                    "Eventos_{0:yyyyMMdd_HHmmss}.log",
                                    DateTime.Now))

                    File.Move(RutaLog, respaldo)

                    'File.Delete(RutaLog)

                End If

            End If

            Dim linea As String = String.Format(
                                    "{0:yyyy-MM-dd HH:mm:ss} - {1}",
                                    DateTime.Now,
                                    Mensaje)

            File.AppendAllText(RutaLog, linea & Environment.NewLine)

        Catch ex As Exception
            ' Ignorar errores del log
        End Try

    End Sub

End Class
