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

    ' =========================================================================
    ' Control de Notificación de Informe Ejecutivo de Seguimiento de Proyectos
    ' =========================================================================
    Private _procesandoInformeProyectos As Boolean = False
    Private _fechaUltimoEnvioInformeProyectos As Nullable(Of DateTime) = Nothing

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
            Dim cadena_conexion_admin As String = "Server=histomedic.mx;Database=mirtheda_admin;Uid=mirtheda_root_________________;Pwd=Bsapmd2cKb*5;SSL Mode=None;"
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
            ' Notificaciones Automáticas a Compras (FLOWserve y DIVERSOS) e Informe Ejecutivo
            ' ======================================
            Try
                If Not _procesandoNotificaciones AndAlso (DateTime.Now.Subtract(_ultimoChequeoNotificaciones).TotalMinutes >= 1) Then
                    _ultimoChequeoNotificaciones = DateTime.Now
                    Me.NotificarCotizacionesPendientesFlowserve()
                    Me.NotificarCotizacionesPendientesDiversos()
                End If

                ' Notificación Diaria del Informe Ejecutivo de Seguimiento de Proyectos
                If Not _procesandoInformeProyectos Then
                    Me.NotificarInformeEjecutivoProyectos()
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

        ' 0. Validar que la hora actual sea a partir de las 8:30 AM
        Dim horaProgramada As New TimeSpan(8, 30, 0)
        If DateTime.Now.TimeOfDay < horaProgramada Then
            ' Aún no son las 8:30 AM del día actual, esperar a la hora programada
            Return
        End If

        _procesandoNotificaciones = True
        Try
            If cx_MySQL_local.State <> ConnectionState.Open Then
                Try
                    If cx_MySQL_local.State = ConnectionState.Broken Then cx_MySQL_local.Close()
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
                ' A. Si ya fue enviada hoy, no volver a enviar en el mismo día
                If fechaUltimaNotif.Value.Date = DateTime.Now.Date Then
                    Return
                End If

                ' B. Validar si ya transcurrieron los días requeridos por la frecuencia configurada
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
                "  COALESCE(cli.nombre_comercial, cli.razon_social, '') AS cliente_nombre, " & _
                "  COALESCE(v.cliente_final, '') AS cliente_final, " & _
                "  COALESCE(p.cDatGenRazonSocial, p.cDatGenNombreAbreviado, 'PROVEEDOR NO ASIGNADO') AS proveedor_nombre, " & _
                "  cd.id AS detalle_id, " & _
                "  CAST(COALESCE(NULLIF(vd.codigo_partida, ''), cd.venta_detalle_id_partida, cd.id) AS CHAR) AS partida_num, " & _
                "  COALESCE(cd.descripcion_proveedor, '') AS descripcion_proveedor, " & _
                "  COALESCE(cd.descripcion_adicional, '') AS descripcion_adicional, " & _
                "  COALESCE(cd.cantidad, 0) AS cantidad, " & _
                "  COALESCE(cd.ccveunidad, 'pza') AS unidad, " & _
                "  COALESCE(cd.codigo_proveedor, '') AS codigo_proveedor, " & _
                "  COALESCE(cd.num_parte, '') AS num_parte, " & _
                "  COALESCE(cd.ccvematerial, '') AS ccvematerial, " & _
                "  cd.precio_unitario " & _
                "FROM tb_compras_cotizaciones c " & _
                "INNER JOIN tb_compras_cotizaciones_detalle cd ON c.id = cd.cotizacion_id " & _
                "INNER JOIN tb_ventas v ON c.venta_id = v.id " & _
                "LEFT JOIN cat_clientes cli ON v.cliente_id = cli.id " & _
                "LEFT JOIN tb_ventas_detalle vd ON cd.venta_detalle_id_partida = vd.id " & _
                "LEFT JOIN cat_clasificacion_proyectos cp ON v.clasificacion_proyecto_id = cp.id " & _
                "LEFT JOIN tb_proveedores p ON c.proveedor_id = p.icveProveedor " & _
                "WHERE c.enviado = 0 and c.omitir_informe = 0 " & _
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
    ''' Genera el análisis ejecutivo automático basado exclusivamente en los datos recopilados del periodo actual.
    ''' Identifica totales, distribución, proyectos/proveedores críticos, antigüedad, alertas y recomendaciones concretas.
    ''' </summary>
    Private Function GenerarResumenEjecutivoHtml(ByVal tipoNotificacion As String, ByVal dt As DataTable, ByVal totalSolicitudesCount As Integer) As String
        Dim sb As New System.Text.StringBuilder()
        Dim totalPartidas As Integer = dt.Rows.Count
        If totalPartidas = 0 Then Return ""

        ' -------------------------------------------------------------
        ' 1. Cálculos Estadísticos y Agrupaciones Derivadas de los Datos
        ' -------------------------------------------------------------
        ' A. Clasificaciones
        Dim dicClasif As New Dictionary(Of String, Integer)()
        For Each r As DataRow In dt.Rows
            Dim cName As String = If(Not IsDBNull(r("clasificacion_nombre")), r("clasificacion_nombre").ToString().Trim(), "SIN CLASIFICACIÓN")
            If Not dicClasif.ContainsKey(cName) Then dicClasif(cName) = 0
            dicClasif(cName) += 1
        Next
        Dim listClasif = dicClasif.OrderByDescending(Function(kvp) kvp.Value).ToList()
        Dim topClasifNombre As String = listClasif(0).Key
        Dim topClasifCount As Integer = listClasif(0).Value
        Dim topClasifPct As Double = Math.Round((CDbl(topClasifCount) / CDbl(totalPartidas)) * 100.0, 1)

        ' B. Proyectos
        Dim dicPry As New Dictionary(Of String, Tuple(Of String, Integer))()
        For Each r As DataRow In dt.Rows
            Dim pryId As String = If(Not IsDBNull(r("proyecto_id")), r("proyecto_id").ToString().Trim(), "SIN-PROYECTO")
            Dim pryTit As String = If(Not IsDBNull(r("proyecto_titulo")), r("proyecto_titulo").ToString().Trim(), "")
            If Not dicPry.ContainsKey(pryId) Then
                dicPry(pryId) = New Tuple(Of String, Integer)(pryTit, 0)
            End If
            dicPry(pryId) = New Tuple(Of String, Integer)(pryTit, dicPry(pryId).Item2 + 1)
        Next
        Dim listPry = dicPry.OrderByDescending(Function(kvp) kvp.Value.Item2).ToList()
        Dim topPryId As String = listPry(0).Key
        Dim topPryTitulo As String = listPry(0).Value.Item1
        Dim topPryCount As Integer = listPry(0).Value.Item2
        Dim topPryPct As Double = Math.Round((CDbl(topPryCount) / CDbl(totalPartidas)) * 100.0, 1)

        ' C. Proveedores
        Dim dicProvPartidas As New Dictionary(Of String, Integer)()
        Dim dicProvSols As New Dictionary(Of String, HashSet(Of String))()
        For Each r As DataRow In dt.Rows
            Dim prov As String = If(Not IsDBNull(r("proveedor_nombre")), r("proveedor_nombre").ToString().Trim(), "PROVEEDOR NO ASIGNADO")
            If String.IsNullOrWhiteSpace(prov) Then prov = "PROVEEDOR NO ASIGNADO"
            Dim solId As String = r("cotizacion_id").ToString()
            If Not dicProvPartidas.ContainsKey(prov) Then
                dicProvPartidas(prov) = 0
                dicProvSols(prov) = New HashSet(Of String)()
            End If
            dicProvPartidas(prov) += 1
            dicProvSols(prov).Add(solId)
        Next
        Dim listProv = dicProvPartidas.OrderByDescending(Function(kvp) kvp.Value).ToList()
        Dim topProvNombre As String = listProv(0).Key
        Dim topProvCount As Integer = listProv(0).Value
        Dim topProvPct As Double = Math.Round((CDbl(topProvCount) / CDbl(totalPartidas)) * 100.0, 1)
        Dim topProvSolsCount As Integer = dicProvSols(topProvNombre).Count

        ' D. Solicitudes por volumen
        Dim dicSolsPartidas As New Dictionary(Of String, Tuple(Of String, String, DateTime?, Integer))()
        For Each r As DataRow In dt.Rows
            Dim cotId As String = r("cotizacion_id").ToString()
            Dim folioSol As String = If(Not IsDBNull(r("folio_solicitud")), r("folio_solicitud").ToString().Trim(), "ID #" & cotId)
            Dim prov As String = If(Not IsDBNull(r("proveedor_nombre")), r("proveedor_nombre").ToString().Trim(), "PROVEEDOR NO ASIGNADO")
            Dim fch As DateTime? = Nothing
            If Not IsDBNull(r("fecha_solicitud")) Then
                Dim tmpF As DateTime
                If DateTime.TryParse(r("fecha_solicitud").ToString(), tmpF) Then fch = tmpF
            End If

            If Not dicSolsPartidas.ContainsKey(cotId) Then
                dicSolsPartidas(cotId) = New Tuple(Of String, String, DateTime?, Integer)(folioSol, prov, fch, 0)
            End If
            Dim curr = dicSolsPartidas(cotId)
            dicSolsPartidas(cotId) = New Tuple(Of String, String, DateTime?, Integer)(curr.Item1, curr.Item2, curr.Item3, curr.Item4 + 1)
        Next
        Dim listSolsVol = dicSolsPartidas.OrderByDescending(Function(kvp) kvp.Value.Item4).ToList()
        Dim topSolItem = listSolsVol(0).Value

        ' E. Antigüedad
        Dim maxDiasAntig As Integer = 0
        Dim solMasAntiguaFolio As String = ""
        Dim fechaMasAntiguaStr As String = ""
        Dim solsConFecha = dicSolsPartidas.Values.Where(Function(s) s.Item3.HasValue).OrderBy(Function(s) s.Item3.Value).ToList()
        If solsConFecha.Count > 0 Then
            Dim oldest = solsConFecha(0)
            maxDiasAntig = CInt(Math.Floor((DateTime.Now.Date - oldest.Item3.Value.Date).TotalDays))
            solMasAntiguaFolio = oldest.Item1
            fechaMasAntiguaStr = oldest.Item3.Value.ToString("dd/MM/yyyy")
        End If

        ' F. Detección de Situaciones Relevantes
        Dim sinProvCount As Integer = If(dicProvPartidas.ContainsKey("PROVEEDOR NO ASIGNADO"), dicProvPartidas("PROVEEDOR NO ASIGNADO"), 0)

        Dim solsMas10Dias As Integer = 0
        For Each s In dicSolsPartidas.Values
            If s.Item3.HasValue AndAlso (DateTime.Now.Date - s.Item3.Value.Date).TotalDays >= 10 Then
                solsMas10Dias += 1
            End If
        Next

        ' -------------------------------------------------------------
        ' 2. Construcción del HTML
        ' -------------------------------------------------------------
        sb.AppendLine("    <!-- SECCIÓN: ANÁLISIS EJECUTIVO -->")
        sb.AppendLine("    <div style=""background-color: #ffffff; border: 1px solid #94a3b8; border-left: 6px solid #0284c7; border-radius: 8px; margin-bottom: 28px; box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.07); overflow: hidden;"">")

        ' Encabezado de la tarjeta ejecutiva
        sb.AppendLine("      <div style=""background-color: #f8fafc; background-image: linear-gradient(135deg, #f8fafc 0%, #f1f5f9 100%); padding: 16px 20px; border-bottom: 1px solid #e2e8f0;"">")
        sb.AppendLine("        <table style=""width: 100%; border-collapse: collapse;"">")
        sb.AppendLine("          <tr>")
        sb.AppendLine("            <td>")
        sb.AppendLine("              <div style=""font-size: 15px; font-weight: 800; color: #0f172a; text-transform: uppercase; letter-spacing: 0.5px;"">&#128202; ANÁLISIS EJECUTIVO</div>")
        sb.AppendLine("              <div style=""font-size: 12px; color: #64748b; margin-top: 2px;"">Diagnóstico automático y recomendaciones estratégicas orientadas a la toma de decisiones</div>")
        sb.AppendLine("            </td>")
        sb.AppendLine(String.Format("            <td style=""text-align: right; font-size: 11px; color: #475569;"">Periodo: <strong>{0}</strong></td>", DateTime.Now.ToString("dd/MM/yyyy")))
        sb.AppendLine("          </tr>")
        sb.AppendLine("        </table>")
        sb.AppendLine("      </div>")

        sb.AppendLine("      <div style=""padding: 20px;"">")

        ' Cuadrícula de Métricas Clave (4 cajas)
        sb.AppendLine("        <table style=""width: 100%; border-collapse: separate; border-spacing: 10px; margin-left: -10px; margin-right: -10px; margin-bottom: 18px;"">")
        sb.AppendLine("          <tr>")
        ' Tarjeta 1
        sb.AppendLine("            <td style=""width: 25%; background-color: #f1f5f9; border: 1px solid #cbd5e1; border-radius: 6px; padding: 12px; vertical-align: top;"">")
        sb.AppendLine("              <div style=""font-size: 11px; color: #64748b; text-transform: uppercase; font-weight: 600;"">Solicitudes Pendientes</div>")
        sb.AppendLine(String.Format("              <div style=""font-size: 20px; font-weight: 800; color: #0f172a; margin-top: 4px;"">{0}</div>", totalSolicitudesCount))
        sb.AppendLine(String.Format("              <div style=""font-size: 11px; color: #475569; margin-top: 2px;"">{0} partidas en total</div>", totalPartidas))
        sb.AppendLine("            </td>")
        ' Tarjeta 2
        sb.AppendLine("            <td style=""width: 25%; background-color: #f1f5f9; border: 1px solid #cbd5e1; border-radius: 6px; padding: 12px; vertical-align: top;"">")
        sb.AppendLine("              <div style=""font-size: 11px; color: #64748b; text-transform: uppercase; font-weight: 600;"">Clasificación Principal</div>")
        sb.AppendLine(String.Format("              <div style=""font-size: 14px; font-weight: 800; color: #1e3a8a; margin-top: 4px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;"">{0}</div>", System.Net.WebUtility.HtmlEncode(topClasifNombre)))
        sb.AppendLine(String.Format("              <div style=""font-size: 11px; color: #475569; margin-top: 2px;"">{0} partidas ({1}%)</div>", topClasifCount, topClasifPct))
        sb.AppendLine("            </td>")
        ' Tarjeta 3
        sb.AppendLine("            <td style=""width: 25%; background-color: #f1f5f9; border: 1px solid #cbd5e1; border-radius: 6px; padding: 12px; vertical-align: top;"">")
        sb.AppendLine("              <div style=""font-size: 11px; color: #64748b; text-transform: uppercase; font-weight: 600;"">Proveedor</div>")
        sb.AppendLine(String.Format("              <div style=""font-size: 14px; font-weight: 800; color: #0f172a; margin-top: 4px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;"">{0}</div>", System.Net.WebUtility.HtmlEncode(topProvNombre)))
        sb.AppendLine(String.Format("              <div style=""font-size: 11px; color: #475569; margin-top: 2px;"">{0} partidas ({1}%)</div>", topProvCount, topProvPct))
        sb.AppendLine("            </td>")
        ' Tarjeta 4
        sb.AppendLine("            <td style=""width: 25%; background-color: #f1f5f9; border: 1px solid #cbd5e1; border-radius: 6px; padding: 12px; vertical-align: top;"">")
        sb.AppendLine("              <div style=""font-size: 11px; color: #64748b; text-transform: uppercase; font-weight: 600;"">Antigüedad Máxima</div>")
        sb.AppendLine(String.Format("              <div style=""font-size: 20px; font-weight: 800; color: {0}; margin-top: 4px;"">{1} días</div>", If(maxDiasAntig >= 10, "#b91c1c", "#0f172a"), maxDiasAntig))
        sb.AppendLine(String.Format("              <div style=""font-size: 11px; color: #475569; margin-top: 2px;"">{0}</div>", If(Not String.IsNullOrWhiteSpace(fechaMasAntiguaStr), "Desde " & fechaMasAntiguaStr, "Sin fecha registrada")))
        sb.AppendLine("            </td>")
        sb.AppendLine("          </tr>")
        sb.AppendLine("        </table>")

        ' Hallazgos Clave de Concentración y Distribución
        sb.AppendLine("        <div style=""margin-bottom: 18px;"">")
        sb.AppendLine("          <div style=""font-size: 13px; font-weight: 700; color: #0f172a; margin-bottom: 8px;"">&#128269; CONCENTRACIONES Y TENDENCIAS CLAVE</div>")
        sb.AppendLine("          <ul style=""margin: 0; padding-left: 18px; font-size: 12px; color: #334155; line-height: 1.6;"">")

        ' Viñeta 1: Distribución por clasificación
        Dim resumenClasifStr As String = String.Join(", ", listClasif.Select(Function(c) String.Format("{0}: {1} ({2}%)", System.Net.WebUtility.HtmlEncode(c.Key), c.Value, Math.Round((CDbl(c.Value) / CDbl(totalPartidas)) * 100.0, 1))))
        sb.AppendLine(String.Format("            <li><strong>Distribución por Línea:</strong> {0}.</li>", resumenClasifStr))

        ' Viñeta 2: Proyectos con mayor volumen
        Dim topPryDetalle As String = String.Join(", ", listPry.Take(3).Select(Function(p) String.Format("{0} ({1} partidas, {2}%)", System.Net.WebUtility.HtmlEncode(p.Key), p.Value.Item2, Math.Round((CDbl(p.Value.Item2) / CDbl(totalPartidas)) * 100.0, 1))))
        sb.AppendLine(String.Format("            <li><strong>Proyectos con Mayor Carga:</strong> {0}.</li>", topPryDetalle))

        ' Viñeta 3: Solicitudes críticas en volumen
        Dim topSolsDetalle As String = String.Join(", ", listSolsVol.Take(3).Select(Function(s) String.Format("{0} ({1} partidas - {2})", System.Net.WebUtility.HtmlEncode(s.Value.Item1), s.Value.Item4, System.Net.WebUtility.HtmlEncode(s.Value.Item2))))
        sb.AppendLine(String.Format("            <li><strong>Solicitudes con Mayor Número de Partidas:</strong> {0}.</li>", topSolsDetalle))

        ' Viñeta 4: Antigüedad
        If maxDiasAntig > 0 Then
            sb.AppendLine(String.Format("            <li><strong>Antigüedad Crítica:</strong> La solicitud más rezagada es <strong>{0}</strong> ({1} días de espera desde el {2}). {3}</li>",
                                        System.Net.WebUtility.HtmlEncode(solMasAntiguaFolio), maxDiasAntig, fechaMasAntiguaStr,
                                        If(solsMas10Dias > 1, String.Format("Existen {0} solicitudes con 10 o más días sin cotizar.", solsMas10Dias), "")))
        End If

        sb.AppendLine("          </ul>")
        sb.AppendLine("        </div>")


        ' Recomendaciones Concretas para Personal de Compras
        sb.AppendLine("        <div style=""background-color: #f0fdf4; border: 1px solid #bbf7d0; border-left: 4px solid #16a34a; border-radius: 4px; padding: 12px 16px;"">")
        sb.AppendLine("          <div style=""font-size: 12px; font-weight: 700; color: #166534; margin-bottom: 6px;"">&#9989; PRIORIDADES DE ATENCIÓN Y RECOMENDACIONES DE COMPRAS:</div>")
        sb.AppendLine("          <ol style=""margin: 0; padding-left: 18px; font-size: 12px; color: #14532d; line-height: 1.6;"">")

        ' Recomendación 1: Solicitud con mayor volumen
        sb.AppendLine(String.Format("            <li><strong>Gestionar con prioridad la solicitud {0}</strong> ({1}): Agrupa {2} partidas pendientes ({3}% del total). Su cotización resolverá la mayor parte del requerimiento actual.</li>",
                                    System.Net.WebUtility.HtmlEncode(topSolItem.Item1), System.Net.WebUtility.HtmlEncode(topSolItem.Item2), topSolItem.Item4, Math.Round((CDbl(topSolItem.Item4) / CDbl(totalPartidas)) * 100.0, 1)))

        ' Recomendación 2: Solicitud más antigua (si es distinta a la de mayor volumen)
        If maxDiasAntig > 0 AndAlso Not solMasAntiguaFolio.Equals(topSolItem.Item1, StringComparison.OrdinalIgnoreCase) Then
            sb.AppendLine(String.Format("            <li><strong>Dar seguimiento por antigüedad a la solicitud {0}</strong>: Registra {1} días en espera desde el {2}. Se aconseja contactar al proveedor para evitar vencimiento en la oferta comercial al cliente.</li>",
                                        System.Net.WebUtility.HtmlEncode(solMasAntiguaFolio), maxDiasAntig, fechaMasAntiguaStr))
        End If

        ' Recomendación 3: Proveedor no asignado si existe
        If sinProvCount > 0 Then
            sb.AppendLine("            <li><strong>Asignar proveedor formal a las solicitudes pendientes</strong>: Existen partidas huérfanas de proveedor que requieren asignación en el catálogo para detonar el proceso de cotización.</li>")
        End If

        ' Recomendación 4: Proyecto principal
        If topPryPct >= 40.0 Then
            sb.AppendLine(String.Format("            <li><strong>Focalizar esfuerzo en el proyecto {0}</strong> ({1}): Representa {2} de las {3} partidas pendientes en compras.</li>",
                                        System.Net.WebUtility.HtmlEncode(topPryId), System.Net.WebUtility.HtmlEncode(topPryTitulo), topPryCount, totalPartidas))
        End If

        sb.AppendLine("          </ol>")
        sb.AppendLine("        </div>")

        sb.AppendLine("      </div>")
        sb.AppendLine("    </div>")

        Return sb.ToString()
    End Function

    ''' <summary>
    ''' Construye el cuerpo del correo en HTML jerárquico:
    ''' Resumen Ejecutivo -> Clasificación de Proyecto -> Solicitud de Cotización -> Partidas Pendientes
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
        sb.AppendLine("  .header { background-color: #1e3a8a; background-image: linear-gradient(135deg, #0f172a 0%, #1e3a8a 100%); color: #ffffff; padding: 24px 30px; text-align: left; }")
        sb.AppendLine("  .header h1 { margin: 0 0 6px 0; font-size: 20px; font-weight: 700; letter-spacing: -0.5px; color: #ffffff; }")
        sb.AppendLine("  .header p { margin: 0; font-size: 13px; color: #cbd5e1; }")
        sb.AppendLine("  .stats-bar { width: 100%; background-color: #f8fafc; border-bottom: 1px solid #e2e8f0; }")
        sb.AppendLine("  .stat-item { font-size: 12px; color: #475569; }")
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
        sb.AppendLine("")
        sb.AppendLine("  <!-- Encabezado principal con soporte para Outlook y Webmail -->")
        sb.AppendLine("  <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""#1e3a8a"" class=""header"" style=""width: 100%; border-collapse: collapse; background-color: #1e3a8a; background-image: linear-gradient(135deg, #0f172a 0%, #1e3a8a 100%);"">")
        sb.AppendLine("    <tr>")
        sb.AppendLine("      <td style=""padding: 24px 30px; text-align: left;"">")
        sb.AppendLine(String.Format("        <h1 style=""margin: 0 0 6px 0; font-size: 20px; font-weight: 700; letter-spacing: -0.5px; color: #ffffff; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;"">Notificación de Cotizaciones Pendientes - {0}</h1>", System.Net.WebUtility.HtmlEncode(tipoNotificacion)))
        sb.AppendLine(String.Format("        <p style=""margin: 0; font-size: 13px; color: #cbd5e1; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;"">Partidas pendientes de cotizar registradas en solicitudes a proveedores &bull; Generado el {0}</p>", DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss")))
        sb.AppendLine("      </td>")
        sb.AppendLine("    </tr>")
        sb.AppendLine("  </table>")
        sb.AppendLine("")
        sb.AppendLine("  <!-- Barra de estadísticas -->")
        sb.AppendLine("  <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""#f8fafc"" class=""stats-bar"" style=""width: 100%; border-collapse: collapse; background-color: #f8fafc; border-bottom: 1px solid #e2e8f0;"">")
        sb.AppendLine("    <tr>")
        sb.AppendLine(String.Format("      <td style=""padding: 12px 30px; font-size: 12px; color: #475569; vertical-align: middle; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;"">Solicitudes con pendientes: <span class=""stat-badge"" style=""display: inline-block; background-color: #1e3a8a; color: #ffffff; font-weight: bold; border-radius: 12px; padding: 2px 8px; font-size: 11px; margin-left: 4px;"">{0}</span></td>", totalSolicitudes.Count))
        sb.AppendLine(String.Format("      <td style=""padding: 12px 10px; font-size: 12px; color: #475569; vertical-align: middle; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;"">Total Partidas Pendientes: <span class=""stat-badge"" style=""display: inline-block; background-color: #1e3a8a; color: #ffffff; font-weight: bold; border-radius: 12px; padding: 2px 8px; font-size: 11px; margin-left: 4px;"">{0}</span></td>", dt.Rows.Count))
        sb.AppendLine(String.Format("      <td style=""padding: 12px 30px; font-size: 12px; color: #475569; vertical-align: middle; text-align: right; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;"">Frecuencia programada: <strong style=""color: #1e293b;"">Cada {0} día(s)</strong></td>", frecuenciaDias))
        sb.AppendLine("    </tr>")
        sb.AppendLine("  </table>")
        sb.AppendLine("")
        sb.AppendLine("  <div class=""content"">")

        ' Generar e insertar el Análisis Ejecutivo antes del detalle
        sb.Append(GenerarResumenEjecutivoHtml(tipoNotificacion, dt, totalSolicitudes.Count))

        sb.AppendLine("    <div style=""margin-top: 10px; margin-bottom: 20px; font-size: 15px; font-weight: 700; color: #0f172a; border-bottom: 2px solid #cbd5e1; padding-bottom: 8px;"">&#128203; DETALLE DE SOLICITUDES Y PARTIDAS PENDIENTES</div>")

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
                Dim provNom As String = If(Not IsDBNull(primerRow("proveedor_nombre")), primerRow("proveedor_nombre").ToString().Trim(), "PROVEEDOR NO ASIGNADO")
                Dim oportFlow As String = If(Not IsDBNull(primerRow("oportunidad_flowserve")), primerRow("oportunidad_flowserve").ToString().Trim(), "")
                Dim clieNom As String = If(Not IsDBNull(primerRow("cliente_nombre")), primerRow("cliente_nombre").ToString().Trim(), "")
                Dim clieFinal As String = If(Not IsDBNull(primerRow("cliente_final")), primerRow("cliente_final").ToString().Trim(), "")

                sb.AppendLine("      <div class=""solicitud-card"">")
                sb.AppendLine("        <div class=""solicitud-header"">")
                sb.AppendLine(String.Format("          <div class=""sol-title"">Solicitud: {0}{1} &bull; Proveedor: {2}</div>",
                                            System.Net.WebUtility.HtmlEncode(folioSol),
                                            If(Not String.IsNullOrWhiteSpace(folioCot), " (Cotiz: " & System.Net.WebUtility.HtmlEncode(folioCot) & ")", ""),
                                            System.Net.WebUtility.HtmlEncode(provNom)))

                sb.AppendLine("          <div class=""sol-meta"">")
                sb.AppendLine(String.Format("            <strong>Proyecto:</strong> {0} &bull; <strong>Fecha Solicitud:</strong> {1}",
                                            System.Net.WebUtility.HtmlEncode(pryId),
                                            System.Net.WebUtility.HtmlEncode(fchSolStr)))

                If Not String.IsNullOrWhiteSpace(clieNom) Then
                    sb.AppendLine(String.Format(" &bull; <strong>Cliente:</strong> {0}", System.Net.WebUtility.HtmlEncode(clieNom)))
                End If
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
                sb.AppendLine("              <th style=""width: 12%; text-align: center;"">Partida</th>")
                sb.AppendLine("              <th style=""width: 12%; text-align: center;"">Cantidad</th>")
                sb.AppendLine("              <th style=""width: 13%;"">Cód. Prov.</th>")
                sb.AppendLine("              <th style=""width: 15%;"">No. Parte</th>")
                sb.AppendLine("              <th style=""width: 48%;"">Descripción / Concepto</th>")
                sb.AppendLine("            </tr>")
                sb.AppendLine("          </thead>")
                sb.AppendLine("          <tbody>")

                For Each r In rowsCot
                    Dim partidaNum As String = "-"
                    If Not IsDBNull(r("partida_num")) Then
                        If TypeOf r("partida_num") Is Byte() Then
                            partidaNum = System.Text.Encoding.UTF8.GetString(DirectCast(r("partida_num"), Byte())).Trim()
                        Else
                            partidaNum = r("partida_num").ToString().Trim()
                        End If
                        If String.IsNullOrWhiteSpace(partidaNum) Then partidaNum = "-"
                    End If
                    Dim cantVal As Double = If(Not IsDBNull(r("cantidad")), Convert.ToDouble(r("cantidad")), 0)
                    Dim unidadStr As String = If(Not IsDBNull(r("unidad")), r("unidad").ToString().Trim(), "pza")
                    Dim codProv As String = If(Not IsDBNull(r("codigo_proveedor")), r("codigo_proveedor").ToString().Trim(), "")
                    Dim numParte As String = If(Not IsDBNull(r("num_parte")), r("num_parte").ToString().Trim(), "")
                    Dim descProv As String = If(Not IsDBNull(r("descripcion_proveedor")), r("descripcion_proveedor").ToString().Trim(), "")
                    Dim descAdic As String = If(Not IsDBNull(r("descripcion_adicional")), r("descripcion_adicional").ToString().Trim(), "")

                    sb.AppendLine("            <tr>")
                    sb.AppendLine(String.Format("              <td style=""text-align: center; font-weight: bold; color: #1e293b;"">{0}</td>", System.Net.WebUtility.HtmlEncode(partidaNum)))
                    sb.AppendLine(String.Format("              <td style=""text-align: center; font-weight: bold;"">{0:N2} {1}</td>", cantVal, System.Net.WebUtility.HtmlEncode(unidadStr)))
                    sb.AppendLine(String.Format("              <td>{0}</td>", If(Not String.IsNullOrWhiteSpace(codProv), "<span class=""tag-code"">" & System.Net.WebUtility.HtmlEncode(codProv) & "</span>", "-")))
                    sb.AppendLine(String.Format("              <td>{0}</td>", If(Not String.IsNullOrWhiteSpace(numParte), "<span class=""tag-code"">" & System.Net.WebUtility.HtmlEncode(numParte) & "</span>", "-")))

                    sb.Append("              <td>")
                    sb.Append(System.Net.WebUtility.HtmlEncode(descProv))
                    If Not String.IsNullOrWhiteSpace(descAdic) AndAlso Not descAdic.Equals(descProv, StringComparison.OrdinalIgnoreCase) Then
                        sb.Append(String.Format("<div class=""desc-adic"">{0}</div>", System.Net.WebUtility.HtmlEncode(descAdic)))
                    End If
                    sb.AppendLine("</td>")
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
        sb.AppendLine("    <p style=""margin: 0 0 4px 0; font-weight: 600;"">LFM RPA Robot &bull; Notificación Automática de Partidas Pendientes de Cotizar. Powered by HistoMedic.</p>")
        sb.AppendLine("    <p style=""margin: 0;"">Este mensaje fue generado automáticamente según la frecuencia programada en configuración general. Por favor no responder a este correo.</p>")
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

#Region "Informe Ejecutivo de Seguimiento de Proyectos"

    ''' <summary>
    ''' Modelo de datos para representar cada proyecto en el Informe Ejecutivo.
    ''' </summary>
    Public Class ItemProyectoInforme
        Public Property VentaId As Integer
        Public Property ProyectoId As String
        Public Property Titulo As String
        Public Property ClasificacionId As Integer
        Public Property ClasificacionNombre As String
        Public Property VendedorClave As String
        Public Property VendedorNombre As String
        Public Property ClienteNombre As String
        Public Property ClienteFinal As String
        Public Property EstatusId As Integer
        Public Property EstatusNombre As String
        Public Property FechaCreacion As DateTime
        Public Property FechaProyecto As Nullable(Of DateTime)
        Public Property MonedaId As Integer
        Public Property MonedaSiglas As String
        Public Property TotalMonto As Double
        Public Property FechaUltimoMovimiento As DateTime
        Public Property DiasSinMovimiento As Integer
        Public Property FechaCompromiso As Nullable(Of DateTime)
        Public Property TipoFechaCompromiso As String
        Public Property DiasParaCompromiso As Nullable(Of Integer)
        Public Property ProximaAccion As String
        Public Property Responsable As String
        Public Property IndicadorRiesgo As String
        Public Property MotivoPrioridad As String
        Public Property Semaforo As String ' VERDE, AMARILLO, ROJO
        Public Property TotalCotizacionesCliente As Integer
        Public Property TotalPedidosCliente As Integer
        Public Property TotalSolicitudesProveedor As Integer
        Public Property TotalSolicitudesProveedorEnviadas As Integer
        Public Property Enviada As Integer
        Public Property Activo As String = "ACTIVO"
        Public Property MotivoCancelacion As String = ""
        Public Property Seguimientos As New List(Of ItemSeguimientoProyecto)()

        Public ReadOnly Property PendienteCotizacionInterna As Boolean
            Get
                Return EstatusId = 3 AndAlso TotalSolicitudesProveedorEnviadas > 0
            End Get
        End Property

        Public ReadOnly Property EsDeclinado As Boolean
            Get
                Return Activo.Equals("CERRADO", StringComparison.OrdinalIgnoreCase)
            End Get
        End Property

        Public ReadOnly Property EsCancelado As Boolean
            Get
                Return EstatusId = 2
            End Get
        End Property

        Public ReadOnly Property EsCanceladoODeclinado As Boolean
            Get
                Return EsDeclinado OrElse EsCancelado
            End Get
        End Property
    End Class

    ''' <summary>
    ''' Modelo para representar cada nota de bitácora registrada en tb_ventas_seguimiento.
    ''' </summary>
    Public Class ItemSeguimientoProyecto
        Public Property Id As Integer
        Public Property Fecha As DateTime
        Public Property UsuarioClave As String
        Public Property UsuarioNombre As String
        Public Property Detalle As String
    End Class

    ''' <summary>
    ''' Rutina principal para generar y notificar diariamente al personal directivo y jefes de área
    ''' el Informe Ejecutivo de Seguimiento de Proyectos (a partir del 24/08/2026, etapas 1 a 7).
    ''' </summary>
    Public Sub NotificarInformeEjecutivoProyectos(Optional ByVal forzarEnvio As Boolean = False)
        If _procesandoInformeProyectos Then Return

        ' 1. Validar horario de envío diario (a partir de las 08:00 AM) salvo si es forzado manualmente
        If Not forzarEnvio Then
            Dim horaProgramada As New TimeSpan(8, 0, 0)
            If DateTime.Now.TimeOfDay < horaProgramada Then
                Return
            End If

            ' Validar que no se haya enviado ya el día de hoy
            If _fechaUltimoEnvioInformeProyectos.HasValue AndAlso _fechaUltimoEnvioInformeProyectos.Value.Date = DateTime.Now.Date Then
                Return
            End If
        End If

        _procesandoInformeProyectos = True
        Try
            If cx_MySQL_local.State <> ConnectionState.Open Then
                Try
                    If cx_MySQL_local.State = ConnectionState.Broken Then cx_MySQL_local.Close()
                    cx_MySQL_local.Open()
                Catch exConn As Exception
                    AgregarLog(500, "[Informe Ejecutivo Proyectos] Error al conectar a la BD local: " & exConn.Message)
                    Return
                End Try
            End If

            ' 2. Asegurar que la tabla histórica exista para almacenar snapshots y comparativos
            AsegurarTablaHistoricaProyectos()

            ' Validar en BD si hoy ya se registró el snapshot (evita envíos duplicados ante reinicios del robot)
            If Not forzarEnvio Then
                Dim sqlCheckHoy As String = "SELECT COUNT(*) FROM tb_informe_proyectos_historico WHERE fecha = CURDATE()"
                Dim dtCheck As DataTable = tb_Recordset_MySQL_local(sqlCheckHoy)
                If dtCheck IsNot Nothing AndAlso dtCheck.Rows.Count > 0 AndAlso Convert.ToInt32(dtCheck.Rows(0)(0)) > 0 Then
                    _fechaUltimoEnvioInformeProyectos = DateTime.Now
                    Return
                End If
            End If

            ' 3. Obtener correos destinatarios del campo correos_copia_proyecto_venta en cat_consultorio
            Dim sqlConsultorio As String = "SELECT correos_copia_proyecto_venta FROM cat_consultorio LIMIT 1"
            Dim dtConsultorio As DataTable = tb_Recordset_MySQL_local(sqlConsultorio)
            If dtConsultorio Is Nothing OrElse dtConsultorio.Rows.Count = 0 OrElse IsDBNull(dtConsultorio.Rows(0)("correos_copia_proyecto_venta")) Then
                AgregarLog(500, "[Informe Ejecutivo Proyectos] No se encontró configuración en cat_consultorio.correos_copia_proyecto_venta.")
                Return
            End If

            Dim destinatarios As String = dtConsultorio.Rows(0)("correos_copia_proyecto_venta").ToString().Trim()
            If String.IsNullOrWhiteSpace(destinatarios) Then
                AgregarLog(500, "[Informe Ejecutivo Proyectos] Omitido: cat_consultorio.correos_copia_proyecto_venta está vacío.")
                Return
            End If

            ' 4. Consultar y evaluar proyectos generados a partir del 24 de agosto de 2026 hasta etapa 7
            Dim listaProyectos As List(Of ItemProyectoInforme) = ConsultarProyectosSeguimiento()
            If listaProyectos.Count = 0 Then
                LogEventos.Escribir("[Informe Ejecutivo Proyectos] No se encontraron proyectos que cumplan con los filtros.")
                Return
            End If

            AgregarLog(100, String.Format("[Informe Ejecutivo Proyectos] Procesando {0} proyectos (a partir del 24/08/2026). Generando informe HTML...", listaProyectos.Count))

            ' 5. Obtener snapshots históricos previos (ayer y hace 7 días) para el comparativo
            Dim dtSnapAyer As DataTable = ObtenerSnapshotHistorico(1)
            Dim dtSnap7Dias As DataTable = ObtenerSnapshotHistorico(7)

            ' 6. Generar el cuerpo HTML completo del informe ejecutivo
            Dim htmlCuerpo As String = GenerarHtmlInformeEjecutivoProyectos(listaProyectos, dtSnapAyer, dtSnap7Dias)
            Dim asunto As String = String.Format("[LFMControl] Informe Ejecutivo de Seguimiento de Proyectos - {0} ({1} Proyectos)",
                                                 DateTime.Now.ToString("dd/MM/yyyy"), listaProyectos.Count)

            ' Guardar respaldo local del HTML generado para consulta y auditoría
            Try
                Dim rutaHtmlLocal As String = System.IO.Path.Combine(Application.StartupPath, "UltimoInformeEjecutivoProyectos.html")
                System.IO.File.WriteAllText(rutaHtmlLocal, htmlCuerpo, System.Text.Encoding.UTF8)
            Catch exFile As Exception
            End Try

            ' 7. Enviar correo a través de Chilkat MailMan
            Dim enviadoExitoso As Boolean = EnviarCorreoNotificacionHTML(destinatarios, asunto, htmlCuerpo)

            If enviadoExitoso Then
                _fechaUltimoEnvioInformeProyectos = DateTime.Now

                ' 8. Guardar snapshot del día en tb_informe_proyectos_historico
                GuardarSnapshotHistoricoProyectos(listaProyectos)

                AgregarLog(200, String.Format("[Informe Ejecutivo Proyectos] Notificación diaria enviada con éxito a: {0} ({1} proyectos reportados).", destinatarios, listaProyectos.Count))
                LogEventos.Escribir(String.Format("[Informe Ejecutivo Proyectos] Notificación enviada exitosamente a: {0}", destinatarios))
            Else
                AgregarLog(500, String.Format("[Informe Ejecutivo Proyectos] Error al enviar correo a: {0}. Se reintentará en el próximo ciclo.", destinatarios))
            End If

        Catch ex As Exception
            AgregarLog(500, "Error en NotificarInformeEjecutivoProyectos: " & ex.Message)
            LogEventos.Escribir("Error en NotificarInformeEjecutivoProyectos: " & ex.Message & " - Stack: " & ex.StackTrace)
        Finally
            _procesandoInformeProyectos = False
        End Try
    End Sub

    ''' <summary>
    ''' Asegura la creación de la tabla histórica de proyectos si aún no existe.
    ''' </summary>
    Private Sub AsegurarTablaHistoricaProyectos()
        Try
            Dim sqlCreate As String =
                "CREATE TABLE IF NOT EXISTS tb_informe_proyectos_historico (" & _
                "  id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY, " & _
                "  fecha DATE NOT NULL, " & _
                "  venta_id INT UNSIGNED NOT NULL, " & _
                "  proyecto_id VARCHAR(45) NOT NULL, " & _
                "  estatus_proyecto_id INT UNSIGNED NOT NULL, " & _
                "  semaforo VARCHAR(15) NOT NULL, " & _
                "  dias_sin_movimiento INT NOT NULL, " & _
                "  monto DOUBLE NOT NULL DEFAULT 0, " & _
                "  moneda VARCHAR(10) NOT NULL DEFAULT 'MXN', " & _
                "  vendedor VARCHAR(150) NOT NULL, " & _
                "  clasificacion VARCHAR(100) NOT NULL, " & _
                "  atrasado TINYINT(1) NOT NULL DEFAULT 0, " & _
                "  fecha_registro DATETIME NOT NULL, " & _
                "  INDEX idx_fecha (fecha), " & _
                "  INDEX idx_proyecto (proyecto_id) " & _
                ");"
            Using cmm As New MySqlConnector.MySqlCommand(sqlCreate, cx_MySQL_local)
                cmm.ExecuteNonQuery()
            End Using
        Catch ex As Exception
            LogEventos.Escribir("Error en AsegurarTablaHistoricaProyectos: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Consulta y procesa todos los proyectos a partir del 24/08/2026 y hasta la etapa 7.
    ''' Calcula fechas de último movimiento, fechas compromiso, días de inactividad y evalúa el semáforo.
    ''' </summary>
    Private Function ConsultarProyectosSeguimiento() As List(Of ItemProyectoInforme)
        Dim resultado As New List(Of ItemProyectoInforme)()

        Dim sqlQuery As String =
            "SELECT " & _
            "  v.id AS venta_id, " & _
            "  COALESCE(v.proyecto_id, '') AS proyecto_id, " & _
            "  COALESCE(v.titulo, '') AS titulo, " & _
            "  COALESCE(cp.id, 0) AS clasificacion_id, " & _
            "  COALESCE(cp.clasificacion, 'SIN CLASIFICACIÓN') AS clasificacion_nombre, " & _
            "  COALESCE(v.ccveusuario_vendedor, '') AS ccveusuario_vendedor, " & _
            "  COALESCE(TRIM(CONCAT_WS(' ', m.cnombre, m.cpriapellido, m.csegapellido)), v.ccveusuario_vendedor, 'SIN ASIGNAR') AS vendedor_nombre, " & _
            "  COALESCE(cli.nombre_comercial, cli.razon_social, 'CLIENTE NO DEFINIDO') AS cliente_nombre, " & _
            "  COALESCE(v.cliente_final, '') AS cliente_final, " & _
            "  v.estatus_proyecto_id, " & _
            "  COALESCE(v.enviada, 0) AS enviada, " & _
            "  COALESCE(ep.cEstatus, 'ESTATUS DESCONOCIDO') AS estatus_nombre, " & _
            "  v.fchregistro AS fecha_creacion, " & _
            "  v.fecha AS fecha_proyecto, " & _
            "  v.moneda_id, " & _
            "  COALESCE(tc.siglas, 'MXN') AS moneda_siglas, " & _
            "  COALESCE(NULLIF(v.total, 0), (SELECT SUM(pc.total) FROM tb_pedidos_cliente pc WHERE pc.venta_id = v.id), (SELECT SUM(cc.total) FROM tb_ventas_cotizacion_cliente cc WHERE cc.venta_id = v.id AND cc.activo = 1), 0) AS total_monto, " & _
            "  (SELECT MAX(fchregistro) FROM tb_ventas_seguimiento WHERE venta_id = v.id) AS ult_seg_fch, " & _
            "  (SELECT MAX(fchregistro) FROM tb_ventas_cotizacion_cliente WHERE venta_id = v.id) AS ult_cot_fch, " & _
            "  (SELECT MAX(fchregistro) FROM tb_pedidos_cliente WHERE venta_id = v.id) AS ult_ped_fch, " & _
            "  (SELECT MAX(fchregistro) FROM tb_compras_cotizaciones WHERE venta_id = v.id) AS ult_compras_fch, " & _
            "  v.fchregistroactualiza AS ult_act_fch, " & _
            "  (SELECT MIN(pcd.fecha_estimada_entrega) FROM tb_pedidos_cliente pc JOIN tb_pedidos_cliente_detalle pcd ON pc.id = pcd.pedido_id WHERE pc.venta_id = v.id AND pcd.fecha_estimada_entrega IS NOT NULL) AS fch_compromiso_cliente, " & _
            "  (SELECT MIN(ppd.fecha_estimada_entrega) FROM tb_pedidos_proveedor pp JOIN tb_pedidos_proveedor_detalle ppd ON pp.id = ppd.pedido_proveedor_id WHERE pp.venta_id = v.id AND ppd.fecha_estimada_entrega IS NOT NULL) AS fch_compromiso_proveedor, " & _
            "  (SELECT MAX(cc.fecha_vigencia) FROM tb_ventas_cotizacion_cliente cc WHERE cc.venta_id = v.id AND cc.activo = 1) AS fch_vigencia_cot, " & _
            "  (SELECT COUNT(*) FROM tb_ventas_cotizacion_cliente cc WHERE cc.venta_id = v.id AND cc.activo = 1) AS total_cotizaciones_cliente, " & _
            "  (SELECT COUNT(*) FROM tb_pedidos_cliente pc WHERE pc.venta_id = v.id) AS total_pedidos_cliente, " & _
            "  (SELECT COUNT(*) FROM tb_compras_cotizaciones com WHERE com.venta_id = v.id) AS total_solicitudes_proveedor, " & _
            "  (SELECT COUNT(*) FROM tb_compras_cotizaciones com WHERE com.venta_id = v.id AND com.enviado = 1) AS total_solicitudes_proveedor_enviadas, " & _
            "  COALESCE(v.activo, 'ACTIVO') AS activo, " & _
            "  COALESCE(v.motivo_cancelacion, '') AS motivo_cancelacion " & _
            "FROM tb_ventas v " & _
            "LEFT JOIN cat_clasificacion_proyectos cp ON v.clasificacion_proyecto_id = cp.id " & _
            "LEFT JOIN cat_medico m ON v.ccveusuario_vendedor = m.ccvemedico " & _
            "LEFT JOIN cat_clientes cli ON v.cliente_id = cli.id " & _
            "LEFT JOIN cat_estatus_proyecto ep ON v.estatus_proyecto_id = ep.Id " & _
            "LEFT JOIN cat_tipos_cambio tc ON v.moneda_id = tc.id " & _
            "WHERE v.estatus_proyecto_id <= 7 " & _
            "  AND v.fecha >= '2026-08-24' " & _
            "ORDER BY cp.clasificacion, vendedor_nombre, v.id DESC;"

        Dim dtProyectos As DataTable = tb_Recordset_MySQL_local(sqlQuery)
        If dtProyectos Is Nothing Then Return resultado

        For Each r As DataRow In dtProyectos.Rows
            Dim item As New ItemProyectoInforme()
            item.VentaId = Convert.ToInt32(r("venta_id"))
            item.ProyectoId = If(Not IsDBNull(r("proyecto_id")), r("proyecto_id").ToString().Trim(), "")
            item.Titulo = If(Not IsDBNull(r("titulo")), r("titulo").ToString().Trim(), "")
            item.ClasificacionId = If(Not IsDBNull(r("clasificacion_id")), Convert.ToInt32(r("clasificacion_id")), 0)
            item.ClasificacionNombre = If(Not IsDBNull(r("clasificacion_nombre")), r("clasificacion_nombre").ToString().Trim(), "SIN CLASIFICACIÓN")
            item.VendedorClave = If(Not IsDBNull(r("ccveusuario_vendedor")), r("ccveusuario_vendedor").ToString().Trim(), "")
            item.VendedorNombre = If(Not IsDBNull(r("vendedor_nombre")), r("vendedor_nombre").ToString().Trim(), "SIN ASIGNAR")
            item.ClienteNombre = If(Not IsDBNull(r("cliente_nombre")), r("cliente_nombre").ToString().Trim(), "CLIENTE NO DEFINIDO")
            item.ClienteFinal = If(Not IsDBNull(r("cliente_final")), r("cliente_final").ToString().Trim(), "")
            item.EstatusId = If(Not IsDBNull(r("estatus_proyecto_id")), Convert.ToInt32(r("estatus_proyecto_id")), 0)
            item.EstatusNombre = If(Not IsDBNull(r("estatus_nombre")), r("estatus_nombre").ToString().Trim(), "ESTATUS DESCONOCIDO")
            item.Activo = If(Not IsDBNull(r("activo")), r("activo").ToString().Trim(), "ACTIVO")
            item.MotivoCancelacion = If(Not IsDBNull(r("motivo_cancelacion")), r("motivo_cancelacion").ToString().Trim(), "")

            ' Fechas base
            Dim fchCrea As DateTime = DateTime.Now
            If Not IsDBNull(r("fecha_creacion")) AndAlso DateTime.TryParse(r("fecha_creacion").ToString(), fchCrea) Then
                item.FechaCreacion = fchCrea
            Else
                item.FechaCreacion = DateTime.Now
            End If

            If Not IsDBNull(r("fecha_proyecto")) Then
                Dim tmpFchPry As DateTime
                If DateTime.TryParse(r("fecha_proyecto").ToString(), tmpFchPry) Then
                    item.FechaProyecto = tmpFchPry
                End If
            End If

            ' Moneda y Monto
            item.MonedaId = If(Not IsDBNull(r("moneda_id")), Convert.ToInt32(r("moneda_id")), 1)
            item.MonedaSiglas = If(Not IsDBNull(r("moneda_siglas")), r("moneda_siglas").ToString().Trim(), "MXN")
            item.TotalMonto = If(Not IsDBNull(r("total_monto")), Convert.ToDouble(r("total_monto")), 0)

            ' Contadores
            item.TotalCotizacionesCliente = If(Not IsDBNull(r("total_cotizaciones_cliente")), Convert.ToInt32(r("total_cotizaciones_cliente")), 0)
            item.TotalPedidosCliente = If(Not IsDBNull(r("total_pedidos_cliente")), Convert.ToInt32(r("total_pedidos_cliente")), 0)
            item.TotalSolicitudesProveedor = If(Not IsDBNull(r("total_solicitudes_proveedor")), Convert.ToInt32(r("total_solicitudes_proveedor")), 0)
            item.TotalSolicitudesProveedorEnviadas = If(Not IsDBNull(r("total_solicitudes_proveedor_enviadas")), Convert.ToInt32(r("total_solicitudes_proveedor_enviadas")), 0)
            item.Enviada = If(Not IsDBNull(r("enviada")), Convert.ToInt32(r("enviada")), 0)

            ' Cálculo de Fecha de Último Movimiento
            Dim fechasMov As New List(Of DateTime)()
            fechasMov.Add(item.FechaCreacion)
            If item.FechaProyecto.HasValue Then fechasMov.Add(item.FechaProyecto.Value)

            If Not IsDBNull(r("ult_act_fch")) Then
                Dim dtTmp As DateTime
                If DateTime.TryParse(r("ult_act_fch").ToString(), dtTmp) Then fechasMov.Add(dtTmp)
            End If
            If Not IsDBNull(r("ult_seg_fch")) Then
                Dim dtTmp As DateTime
                If DateTime.TryParse(r("ult_seg_fch").ToString(), dtTmp) Then fechasMov.Add(dtTmp)
            End If
            If Not IsDBNull(r("ult_cot_fch")) Then
                Dim dtTmp As DateTime
                If DateTime.TryParse(r("ult_cot_fch").ToString(), dtTmp) Then fechasMov.Add(dtTmp)
            End If
            If Not IsDBNull(r("ult_ped_fch")) Then
                Dim dtTmp As DateTime
                If DateTime.TryParse(r("ult_ped_fch").ToString(), dtTmp) Then fechasMov.Add(dtTmp)
            End If
            If Not IsDBNull(r("ult_compras_fch")) Then
                Dim dtTmp As DateTime
                If DateTime.TryParse(r("ult_compras_fch").ToString(), dtTmp) Then fechasMov.Add(dtTmp)
            End If

            item.FechaUltimoMovimiento = fechasMov.Max()
            Dim diasInactivo As Integer = CInt(Math.Floor((DateTime.Now.Date - item.FechaUltimoMovimiento.Date).TotalDays))
            item.DiasSinMovimiento = If(diasInactivo < 0, 0, diasInactivo)

            ' Resolución de Fecha Compromiso
            If Not IsDBNull(r("fch_compromiso_cliente")) Then
                Dim dtComp As DateTime
                If DateTime.TryParse(r("fch_compromiso_cliente").ToString(), dtComp) Then
                    item.FechaCompromiso = dtComp
                    item.TipoFechaCompromiso = "Entrega cliente"
                End If
            ElseIf Not IsDBNull(r("fch_compromiso_proveedor")) Then
                Dim dtComp As DateTime
                If DateTime.TryParse(r("fch_compromiso_proveedor").ToString(), dtComp) Then
                    item.FechaCompromiso = dtComp
                    item.TipoFechaCompromiso = "Entrega proveedor"
                End If
            ElseIf Not IsDBNull(r("fch_vigencia_cot")) Then
                Dim dtComp As DateTime
                If DateTime.TryParse(r("fch_vigencia_cot").ToString(), dtComp) Then
                    item.FechaCompromiso = dtComp
                    item.TipoFechaCompromiso = "Vigencia cotización"
                End If
            End If

            If item.FechaCompromiso.HasValue Then
                item.DiasParaCompromiso = CInt(Math.Floor((item.FechaCompromiso.Value.Date - DateTime.Now.Date).TotalDays))
            End If

            ' Próxima Acción Requerida y Responsable según Estatus y Condición de Cierre
            If item.EsDeclinado Then
                item.ProximaAccion = "Proyecto declinado comercialmente / no procedente"
                item.Responsable = "Ventas (" & item.VendedorNombre & ")"
            ElseIf item.EsCancelado Then
                item.ProximaAccion = If(Not String.IsNullOrWhiteSpace(item.MotivoCancelacion), "Cancelado: " & item.MotivoCancelacion, "Proyecto cancelado / cerrado en bitácora")
                item.Responsable = "Ventas (" & item.VendedorNombre & ")"
            Else
                Select Case item.EstatusId
                    Case 1 ' OPORTUNIDAD DE VENTA
                        If item.Enviada = 1 Then
                            item.ProximaAccion = "En proceso de cotización de compras / seguimiento a cotización de proveedor"
                            item.Responsable = "Compras / Ventas"
                        Else
                            item.ProximaAccion = "Pendiente de información técnica / especificación para cotizar"
                            item.Responsable = "Ventas (" & item.VendedorNombre & ")"
                        End If
                    Case 2 ' CANCELADO
                        item.ProximaAccion = "Proyecto cancelado / cerrado en bitácora"
                        item.Responsable = "Ventas (" & item.VendedorNombre & ")"
                    Case 3 ' COTIZACION DE PROVEEDOR
                        If item.PendienteCotizacionInterna Then
                            item.ProximaAccion = "Elaborar cotización interna (costos de proveedor disponibles)"
                            item.Responsable = "Ventas (" & item.VendedorNombre & ")"
                        Else
                            item.ProximaAccion = "Dar seguimiento a respuesta de proveedores y registrar costos en el sistema"
                            item.Responsable = "Compras / Ventas"
                        End If
                    Case 4 ' COTIZACION INTERNA ELABORADA
                        item.ProximaAccion = "Generar y enviar formalmente la cotización de venta al cliente"
                        item.Responsable = "Ventas (" & item.VendedorNombre & ")"
                    Case 5 ' COTIZACION CLIENTE ELABORADA
                        item.ProximaAccion = "Seguimiento comercial con cliente para cierre de venta y recepción de OC"
                        item.Responsable = "Ventas (" & item.VendedorNombre & ")"
                    Case 6 ' ORDEN COMPRA CLIENTE
                        item.ProximaAccion = "Colocar Orden de Compra formal al proveedor e iniciar aprovisionamiento"
                        item.Responsable = "Compras"
                    Case 7 ' ORDEN COMPRA PROVEEDOR
                        item.ProximaAccion = "Monitorear entrega del proveedor y coordinar recepción e inspección en almacén"
                        item.Responsable = "Compras / Almacén"
                    Case Else
                        item.ProximaAccion = "Seguimiento operativo general del proyecto"
                        item.Responsable = "Ventas / Operaciones"
                End Select
            End If

            ' =====================================================================
            ' Motor de Evaluación de Semáforo Automático (VERDE, AMARILLO, ROJO)
            ' =====================================================================
            EvaluarSemaforoProyecto(item)

            resultado.Add(item)
        Next

        ' Cargar historial de seguimiento registrado en bitácora para proyectos declinados o cancelados
        Dim proyectosConSeguimiento = resultado.Where(Function(p) p.EsCanceladoODeclinado).ToList()
        If proyectosConSeguimiento.Count > 0 Then
            Dim idsVenta = String.Join(",", proyectosConSeguimiento.Select(Function(p) p.VentaId.ToString()))
            Dim sqlSeg As String = String.Format(
                "SELECT s.id, s.venta_id, s.fchregistro, s.ccveusuario, " & _
                "  COALESCE(TRIM(CONCAT_WS(' ', m.cnombre, m.cpriapellido, m.csegapellido)), s.ccveusuario, 'SISTEMA') AS usuario_nombre, " & _
                "  COALESCE(s.seguimiento, '') AS seguimiento " & _
                "FROM tb_ventas_seguimiento s " & _
                "LEFT JOIN cat_medico m ON s.ccveusuario = m.ccvemedico " & _
                "WHERE s.venta_id IN ({0}) " & _
                "ORDER BY s.id ASC;", idsVenta)

            Dim dtSeg As DataTable = tb_Recordset_MySQL_local(sqlSeg)
            If dtSeg IsNot Nothing Then
                For Each rSeg As DataRow In dtSeg.Rows
                    Dim vId As Integer = Convert.ToInt32(rSeg("venta_id"))
                    Dim targetProj = resultado.FirstOrDefault(Function(p) p.VentaId = vId)
                    If targetProj IsNot Nothing Then
                        Dim seg As New ItemSeguimientoProyecto()
                        seg.Id = Convert.ToInt32(rSeg("id"))
                        If Not IsDBNull(rSeg("fchregistro")) Then
                            Dim dtFchSeg As DateTime
                            If DateTime.TryParse(rSeg("fchregistro").ToString(), dtFchSeg) Then
                                seg.Fecha = dtFchSeg
                            End If
                        End If
                        seg.UsuarioClave = If(Not IsDBNull(rSeg("ccveusuario")), rSeg("ccveusuario").ToString().Trim(), "")
                        seg.UsuarioNombre = If(Not IsDBNull(rSeg("usuario_nombre")), rSeg("usuario_nombre").ToString().Trim(), "SISTEMA")
                        seg.Detalle = If(Not IsDBNull(rSeg("seguimiento")), rSeg("seguimiento").ToString().Trim(), "")
                        targetProj.Seguimientos.Add(seg)
                    End If
                Next
            End If
        End If

        Return resultado
    End Function

    ''' <summary>
    ''' Evalúa rigurosamente el semáforo automático del proyecto (VERDE, AMARILLO, ROJO),
    ''' su indicador de riesgo y el motivo de su prioridad.
    ''' </summary>
    Private Sub EvaluarSemaforoProyecto(ByVal item As ItemProyectoInforme)
        ' Regla Especial: Proyecto Declinado (activo = 'CERRADO')
        If item.EsDeclinado Then
            item.Semaforo = "ROJO"
            item.MotivoPrioridad = "Proyecto Declinado (Cerrado)"
            item.IndicadorRiesgo = "DECLINADO"
            Return
        End If

        ' Regla Especial: Proyecto Cancelado (estatus = 2)
        If item.EsCancelado Then
            item.Semaforo = "ROJO"
            item.MotivoPrioridad = If(Not String.IsNullOrWhiteSpace(item.MotivoCancelacion), "Cancelado: " & item.MotivoCancelacion, "Proyecto Cancelado")
            item.IndicadorRiesgo = "CANCELADO"
            Return
        End If

        ' 1. Regla Crítica: Fecha Compromiso Vencida
        If item.FechaCompromiso.HasValue AndAlso item.DiasParaCompromiso.HasValue AndAlso item.DiasParaCompromiso.Value < 0 Then
            item.Semaforo = "ROJO"
            item.MotivoPrioridad = String.Format("Fecha compromiso ({0}) vencida hace {1} día(s)", item.TipoFechaCompromiso, Math.Abs(item.DiasParaCompromiso.Value))
            item.IndicadorRiesgo = "CRÍTICO - Compromiso Vencido"
            Return
        End If

        ' 2. Regla Crítica: Cotización interna lista pero aún no se ha elaborado cotización al cliente (Estatus 4)
        If item.EstatusId = 4 Then
            item.Semaforo = "ROJO"
            item.MotivoPrioridad = "Cotización interna lista, pendiente vendedor elabore cotización a cliente"
            item.IndicadorRiesgo = "ALTO - Pendiente Cotizar a Cliente"
            Return
        End If

        ' 2. Regla Crítica: Inactividad severa (> 15 días sin movimiento)
        If item.DiasSinMovimiento > 15 Then
            item.Semaforo = "ROJO"
            item.MotivoPrioridad = String.Format("Estancamiento severo: {0} días sin ningún movimiento", item.DiasSinMovimiento)
            item.IndicadorRiesgo = "CRÍTICO - Inactividad > 15 días"
            Return
        End If

        ' 3. Regla Crítica: En cotización temprana (1, 3, 4) con más de 7 días sin movimiento
        If (item.EstatusId = 1 OrElse item.EstatusId = 3 OrElse item.EstatusId = 4) AndAlso item.DiasSinMovimiento > 7 Then
            item.Semaforo = "ROJO"
            item.MotivoPrioridad = String.Format("Cotización detenida durante {0} días sin avance", item.DiasSinMovimiento)
            item.IndicadorRiesgo = "ALTO - Retraso en Cotización"
            Return
        End If

        ' 4. Regla Crítica: Orden de Compra Cliente recibida (6) sin colocar a proveedor por más de 3 días
        If item.EstatusId = 6 AndAlso item.DiasSinMovimiento > 3 Then
            item.Semaforo = "ROJO"
            item.MotivoPrioridad = String.Format("OC de cliente recibida hace {0} días sin colocar pedido a proveedor", item.DiasSinMovimiento)
            item.IndicadorRiesgo = "ALTO - Retraso en Colocación a Proveedor"
            Return
        End If

        ' 5. Regla Crítica: Proyectos de alto impacto económico sin movimiento > 5 días
        Dim esAltoValor As Boolean = (item.TotalMonto >= 100000.0) OrElse (item.MonedaSiglas.Equals("USD", StringComparison.OrdinalIgnoreCase) AndAlso item.TotalMonto >= 5000.0)
        If esAltoValor AndAlso item.DiasSinMovimiento > 5 Then
            item.Semaforo = "ROJO"
            item.MotivoPrioridad = String.Format("Proyecto de alto valor ({0:C2} {1}) sin movimiento por {2} días", item.TotalMonto, item.MonedaSiglas, item.DiasSinMovimiento)
            item.IndicadorRiesgo = "ALTO - Impacto Económico Detenido"
            Return
        End If

        ' 6. Regla Preventiva: Fecha compromiso próxima a vencer (0 a 3 días)
        If item.FechaCompromiso.HasValue AndAlso item.DiasParaCompromiso.HasValue AndAlso item.DiasParaCompromiso.Value >= 0 AndAlso item.DiasParaCompromiso.Value <= 3 Then
            item.Semaforo = "AMARILLO"
            If item.DiasParaCompromiso.Value = 0 Then
                item.MotivoPrioridad = String.Format("Fecha compromiso ({0}) vence HOY", item.TipoFechaCompromiso)
            Else
                item.MotivoPrioridad = String.Format("Fecha compromiso ({0}) próxima: vence en {1} día(s)", item.TipoFechaCompromiso, item.DiasParaCompromiso.Value)
            End If
            item.IndicadorRiesgo = "MEDIO - Vencimiento Próximo"
            Return
        End If

        ' 7. Regla Preventiva: Inactividad moderada (4 a 15 días)
        If item.DiasSinMovimiento >= 4 AndAlso item.DiasSinMovimiento <= 15 Then
            item.Semaforo = "AMARILLO"
            item.MotivoPrioridad = String.Format("Seguimiento preventivo: {0} días sin movimiento", item.DiasSinMovimiento)
            item.IndicadorRiesgo = "MEDIO - Sin Movimiento (4-15 días)"
            Return
        End If

        ' 8. Regla Preventiva: Cotización de proveedor en espera por 3 a 7 días
        If item.EstatusId = 3 AndAlso item.DiasSinMovimiento >= 3 Then
            item.Semaforo = "AMARILLO"
            item.MotivoPrioridad = String.Format("En espera de cotización de proveedor durante {0} días", item.DiasSinMovimiento)
            item.IndicadorRiesgo = "MEDIO - Espera Proveedor"
            Return
        End If

        ' 9. Regla Preventiva: Cotización cliente emitida hace 4 a 10 días sin resolución
        If item.EstatusId = 5 AndAlso item.DiasSinMovimiento >= 4 Then
            item.Semaforo = "AMARILLO"
            item.MotivoPrioridad = String.Format("Cotización emitida hace {0} días pendiente de respuesta comercial", item.DiasSinMovimiento)
            item.IndicadorRiesgo = "MEDIO - Seguimiento Comercial"
            Return
        End If

        ' 10. Normal: Proyecto al día
        item.Semaforo = "VERDE"
        item.MotivoPrioridad = String.Format("Actividad reciente ({0} días sin mov), en tiempo", item.DiasSinMovimiento)
        item.IndicadorRiesgo = "BAJO - En Tiempo"
    End Sub

    ''' <summary>
    ''' Guarda el snapshot diario de proyectos en tb_informe_proyectos_historico para respaldar comparativos futuros.
    ''' </summary>
    Private Sub GuardarSnapshotHistoricoProyectos(ByVal proyectos As List(Of ItemProyectoInforme))
        Try
            ' Eliminar snapshot del día de hoy en caso de reejecución para evitar registros duplicados
            Dim sqlDel As String = "DELETE FROM tb_informe_proyectos_historico WHERE fecha = CURDATE()"
            Using cmmDel As New MySqlConnector.MySqlCommand(sqlDel, cx_MySQL_local)
                cmmDel.ExecuteNonQuery()
            End Using

            ' Insertar cada proyecto evaluado
            Dim sqlInsert As String =
                "INSERT INTO tb_informe_proyectos_historico " & _
                "(fecha, venta_id, proyecto_id, estatus_proyecto_id, semaforo, dias_sin_movimiento, monto, moneda, vendedor, clasificacion, atrasado, fecha_registro) " & _
                "VALUES (@fecha, @venta_id, @proyecto_id, @estatus_id, @semaforo, @dias_sin_mov, @monto, @moneda, @vendedor, @clasificacion, @atrasado, NOW());"

            For Each p In proyectos
                Using cmmIns As New MySqlConnector.MySqlCommand(sqlInsert, cx_MySQL_local)
                    cmmIns.Parameters.AddWithValue("@fecha", DateTime.Now.Date)
                    cmmIns.Parameters.AddWithValue("@venta_id", p.VentaId)
                    cmmIns.Parameters.AddWithValue("@proyecto_id", p.ProyectoId)
                    cmmIns.Parameters.AddWithValue("@estatus_id", p.EstatusId)
                    cmmIns.Parameters.AddWithValue("@semaforo", p.Semaforo)
                    cmmIns.Parameters.AddWithValue("@dias_sin_mov", p.DiasSinMovimiento)
                    cmmIns.Parameters.AddWithValue("@monto", p.TotalMonto)
                    cmmIns.Parameters.AddWithValue("@moneda", p.MonedaSiglas)
                    cmmIns.Parameters.AddWithValue("@vendedor", p.VendedorNombre)
                    cmmIns.Parameters.AddWithValue("@clasificacion", p.ClasificacionNombre)
                    cmmIns.Parameters.AddWithValue("@atrasado", If(p.Semaforo = "ROJO", 1, 0))
                    cmmIns.ExecuteNonQuery()
                End Using
            Next
        Catch ex As Exception
            LogEventos.Escribir("Error en GuardarSnapshotHistoricoProyectos: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Recupera el snapshot histórico más cercano según los días de desfase solicitados (1 día atrás o 7 días atrás).
    ''' </summary>
    Private Function ObtenerSnapshotHistorico(ByVal diasAtras As Integer) As DataTable
        Try
            Dim sqlSnap As String = ""
            If diasAtras = 1 Then
                sqlSnap = "SELECT * FROM tb_informe_proyectos_historico WHERE fecha = (SELECT MAX(fecha) FROM tb_informe_proyectos_historico WHERE fecha < CURDATE())"
            Else
                sqlSnap = String.Format("SELECT * FROM tb_informe_proyectos_historico WHERE fecha = (SELECT MAX(fecha) FROM tb_informe_proyectos_historico WHERE fecha <= DATE_SUB(CURDATE(), INTERVAL {0} DAY))", diasAtras)
            End If
            Return tb_Recordset_MySQL_local(sqlSnap)
        Catch ex As Exception
            LogEventos.Escribir("Error en ObtenerSnapshotHistorico: " & ex.Message)
            Return Nothing
        End Try
    End Function

    ''' <summary>
    ''' Construye el cuerpo del correo HTML completo del Informe Ejecutivo de Seguimiento de Proyectos.
    ''' </summary>
    Private Function GenerarHtmlInformeEjecutivoProyectos(ByVal proyectos As List(Of ItemProyectoInforme),
                                                          ByVal dtSnapAyer As DataTable,
                                                          ByVal dtSnap7Dias As DataTable) As String
        Dim sb As New System.Text.StringBuilder()

        ' Métricas globales
        Dim totalProyectos As Integer = proyectos.Count
        Dim verdesCount As Integer = proyectos.Where(Function(p) p.Semaforo = "VERDE").Count()
        Dim amarillosCount As Integer = proyectos.Where(Function(p) p.Semaforo = "AMARILLO").Count()
        Dim rojosCount As Integer = proyectos.Where(Function(p) p.Semaforo = "ROJO").Count()

        Dim pctVerdes As Double = If(totalProyectos > 0, Math.Round((CDbl(verdesCount) / CDbl(totalProyectos)) * 100.0, 1), 0)
        Dim pctAmarillos As Double = If(totalProyectos > 0, Math.Round((CDbl(amarillosCount) / CDbl(totalProyectos)) * 100.0, 1), 0)
        Dim pctRojos As Double = If(totalProyectos > 0, Math.Round((CDbl(rojosCount) / CDbl(totalProyectos)) * 100.0, 1), 0)

        Dim totalMontoUSD As Double = proyectos.Where(Function(p) p.MonedaSiglas.Equals("USD", StringComparison.OrdinalIgnoreCase)).Sum(Function(p) p.TotalMonto)
        Dim totalMontoMXN As Double = proyectos.Where(Function(p) Not p.MonedaSiglas.Equals("USD", StringComparison.OrdinalIgnoreCase)).Sum(Function(p) p.TotalMonto)

        sb.AppendLine("<!DOCTYPE html>")
        sb.AppendLine("<html>")
        sb.AppendLine("<head>")
        sb.AppendLine("<meta http-equiv=""Content-Type"" content=""text/html; charset=utf-8"" />")
        sb.AppendLine("<meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />")
        sb.AppendLine("<style type=""text/css"">")
        sb.AppendLine("  body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #0f172a; margin: 0; padding: 20px; color: #1e293b; }")
        sb.AppendLine("  .wrapper { max-width: 1080px; margin: 0 auto; background-color: #ffffff; border-radius: 10px; overflow: hidden; box-shadow: 0 10px 25px rgba(0, 0, 0, 0.2); }")
        sb.AppendLine("  .main-header { background-color: #0f172a; background: linear-gradient(135deg, #091e42 0%, #1e3a8a 100%); color: #ffffff; padding: 28px 32px; text-align: left; }")
        sb.AppendLine("  .main-header h1 { margin: 0 0 6px 0; font-size: 22px; font-weight: 800; letter-spacing: -0.5px; color: #ffffff !important; }")
        sb.AppendLine("  .main-header p { margin: 0; font-size: 13px; color: #cbd5e1 !important; }")
        sb.AppendLine("  .kpi-banner { width: 100%; border-collapse: collapse; background-color: #f8fafc; border-bottom: 2px solid #e2e8f0; text-align: center; }")
        sb.AppendLine("  .kpi-cell { padding: 14px 10px; border-right: 1px solid #e2e8f0; }")
        sb.AppendLine("  .kpi-label { font-size: 10px; text-transform: uppercase; font-weight: 700; letter-spacing: 0.5px; color: #64748b; margin-bottom: 4px; }")
        sb.AppendLine("  .kpi-value { font-size: 18px; font-weight: 800; }")
        sb.AppendLine("  .kpi-val-tot { color: #1e293b; }")
        sb.AppendLine("  .kpi-val-grn { color: #15803d; }")
        sb.AppendLine("  .kpi-val-yel { color: #b45309; }")
        sb.AppendLine("  .kpi-val-red { color: #b91c1c; }")
        sb.AppendLine("  .kpi-val-mto { color: #0369a1; font-size: 15px; }")
        sb.AppendLine("  .container { padding: 26px 32px; }")
        sb.AppendLine("  .sec-heading { font-size: 16px; font-weight: 700; color: #0f172a; margin: 26px 0 14px 0; padding-bottom: 8px; border-bottom: 2px solid #cbd5e1; display: flex; align-items: center; }")
        sb.AppendLine("  .badge-v { display: inline-block; background-color: #dcfce7; color: #15803d; border: 1px solid #86efac; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px; }")
        sb.AppendLine("  .badge-a { display: inline-block; background-color: #fef9c3; color: #a16207; border: 1px solid #fde047; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px; }")
        sb.AppendLine("  .badge-r { display: inline-block; background-color: #fee2e2; color: #b91c1c; border: 1px solid #fca5a5; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px; }")
        sb.AppendLine("  table.data-table { width: 100%; border-collapse: collapse; font-size: 12px; margin-bottom: 18px; }")
        sb.AppendLine("  table.data-table th { background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 8px 10px; border-bottom: 2px solid #cbd5e1; text-transform: uppercase; font-size: 11px; text-align: left; }")
        sb.AppendLine("  table.data-table td { padding: 8px 10px; border-bottom: 1px solid #e2e8f0; vertical-align: middle; }")
        sb.AppendLine("  table.data-table tr:nth-child(even) { background-color: #f8fafc; }")
        sb.AppendLine("  .clasif-block { margin-bottom: 26px; border: 1px solid #cbd5e1; border-radius: 6px; overflow: hidden; }")
        sb.AppendLine("  .clasif-bar { background-color: #1e3a8a; color: #ffffff; padding: 10px 16px; font-weight: 700; font-size: 14px; text-transform: uppercase; letter-spacing: 0.5px; }")
        sb.AppendLine("  .vendedor-bar { background-color: #e2e8f0; color: #0f172a; padding: 7px 16px; font-weight: 700; font-size: 12px; border-top: 1px solid #cbd5e1; border-bottom: 1px solid #cbd5e1; }")
        sb.AppendLine("  .card-prio { border-left: 5px solid #dc2626; background-color: #fff1f2; padding: 12px 16px; margin-bottom: 12px; border-radius: 4px; border-top: 1px solid #fecdd3; border-right: 1px solid #fecdd3; border-bottom: 1px solid #fecdd3; }")
        sb.AppendLine("  .card-prio-title { font-size: 13px; font-weight: 700; color: #991b1b; margin-bottom: 4px; }")
        sb.AppendLine("  .card-prio-meta { font-size: 11px; color: #475569; line-height: 1.5; }")
        sb.AppendLine("  .analisis-box { background-color: #f0fdf4; border: 1px solid #bbf7d0; border-left: 5px solid #16a34a; border-radius: 6px; padding: 16px 20px; margin-bottom: 22px; font-size: 12px; line-height: 1.6; color: #1e293b; }")
        sb.AppendLine("  .analisis-box ul { margin: 6px 0 0 18px; padding: 0; }")
        sb.AppendLine("  .analisis-box li { margin-bottom: 6px; }")
        sb.AppendLine("  .footer { background-color: #f8fafc; border-top: 1px solid #e2e8f0; padding: 18px 32px; font-size: 11px; color: #64748b; text-align: center; }")
        sb.AppendLine("</style>")
        sb.AppendLine("</head>")
        sb.AppendLine("<body>")
        sb.AppendLine("<div class=""wrapper"" style=""max-width: 1080px; margin: 0 auto; background-color: #ffffff; border-radius: 8px; overflow: hidden; border: 1px solid #cbd5e1;"">")
        sb.AppendLine("")
        sb.AppendLine("  <!-- 1. Header principal -->")
        sb.AppendLine("  <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""#0f172a"" class=""main-header"" style=""width: 100%; border-collapse: collapse; background-color: #0f172a; background: linear-gradient(135deg, #091e42 0%, #1e3a8a 100%);"">")
        sb.AppendLine("    <tr>")
        sb.AppendLine("      <td bgcolor=""#0f172a"" style=""padding: 24px 32px; background-color: #0f172a; text-align: left;"">")
        sb.AppendLine("        <h1 style=""margin: 0 0 6px 0; font-size: 22px; font-weight: 800; letter-spacing: -0.5px; color: #ffffff !important; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;"">Informe Ejecutivo de Seguimiento de Proyectos</h1>")
        sb.AppendLine(String.Format("        <p style=""margin: 0; font-size: 13px; color: #cbd5e1 !important; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;"">Cartera Activa desde el 24 de Agosto de 2026 (Etapas 1 a 7) &bull; Emitido el {0}</p>", DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss")))
        sb.AppendLine("      </td>")
        sb.AppendLine("    </tr>")
        sb.AppendLine("  </table>")
        sb.AppendLine("")
        sb.AppendLine("  <!-- 2. Banner superior de Indicadores Clave (KPI) -->")
        sb.AppendLine("  <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""#f8fafc"" class=""kpi-banner"" style=""width: 100%; border-collapse: collapse; background-color: #f8fafc; border-bottom: 2px solid #e2e8f0; text-align: center;"">")
        sb.AppendLine("    <tr>")
        sb.AppendLine(String.Format("      <td class=""kpi-cell"" bgcolor=""#f8fafc"" style=""padding: 14px 10px; border-right: 1px solid #e2e8f0; text-align: center;""><div class=""kpi-label"" style=""font-size: 10px; text-transform: uppercase; font-weight: 700; letter-spacing: 0.5px; color: #64748b; margin-bottom: 4px;"">Total Proyectos</div><div class=""kpi-value kpi-val-tot"" style=""font-size: 18px; font-weight: 800; color: #1e293b;"">{0}</div></td>", totalProyectos))
        sb.AppendLine(String.Format("      <td class=""kpi-cell"" bgcolor=""#f8fafc"" style=""padding: 14px 10px; border-right: 1px solid #e2e8f0; text-align: center;""><div class=""kpi-label"" style=""font-size: 10px; text-transform: uppercase; font-weight: 700; letter-spacing: 0.5px; color: #64748b; margin-bottom: 4px;"">Verdes</div><div class=""kpi-value kpi-val-grn"" style=""font-size: 18px; font-weight: 800; color: #15803d;"">{0} <span style=""font-size: 11px; font-weight: normal;"">({1}%)</span></div></td>", verdesCount, pctVerdes))
        sb.AppendLine(String.Format("      <td class=""kpi-cell"" bgcolor=""#f8fafc"" style=""padding: 14px 10px; border-right: 1px solid #e2e8f0; text-align: center;""><div class=""kpi-label"" style=""font-size: 10px; text-transform: uppercase; font-weight: 700; letter-spacing: 0.5px; color: #64748b; margin-bottom: 4px;"">Amarillos</div><div class=""kpi-value kpi-val-yel"" style=""font-size: 18px; font-weight: 800; color: #b45309;"">{0} <span style=""font-size: 11px; font-weight: normal;"">({1}%)</span></div></td>", amarillosCount, pctAmarillos))
        sb.AppendLine(String.Format("      <td class=""kpi-cell"" bgcolor=""#f8fafc"" style=""padding: 14px 10px; border-right: 1px solid #e2e8f0; text-align: center;""><div class=""kpi-label"" style=""font-size: 10px; text-transform: uppercase; font-weight: 700; letter-spacing: 0.5px; color: #64748b; margin-bottom: 4px;"">Rojos</div><div class=""kpi-value kpi-val-red"" style=""font-size: 18px; font-weight: 800; color: #b91c1c;"">{0} <span style=""font-size: 11px; font-weight: normal;"">({1}%)</span></div></td>", rojosCount, pctRojos))
        sb.AppendLine(String.Format("      <td class=""kpi-cell"" bgcolor=""#f8fafc"" style=""padding: 14px 10px; border-right: 1px solid #e2e8f0; text-align: center;""><div class=""kpi-label"" style=""font-size: 10px; text-transform: uppercase; font-weight: 700; letter-spacing: 0.5px; color: #64748b; margin-bottom: 4px;"">Pendientes Críticos</div><div class=""kpi-value kpi-val-red"" style=""font-size: 18px; font-weight: 800; color: #b91c1c;"">{0}</div></td>", rojosCount))
        sb.AppendLine(String.Format("      <td class=""kpi-cell"" bgcolor=""#f8fafc"" style=""padding: 14px 10px; border-right: none; text-align: center;""><div class=""kpi-label"" style=""font-size: 10px; text-transform: uppercase; font-weight: 700; letter-spacing: 0.5px; color: #64748b; margin-bottom: 4px;"">Monto en Cartera</div><div class=""kpi-value kpi-val-mto"" style=""font-size: 15px; font-weight: 800; color: #0369a1;"">${0:N0} USD<br/><span style=""font-size: 11px; color: #475569; font-weight: 600;"">${1:N0} MXN</span></div></td>", totalMontoUSD, totalMontoMXN))
        sb.AppendLine("    </tr>")
        sb.AppendLine("  </table>")

        sb.AppendLine("  <div class=""container"">")

        ' 3. Resumen Ejecutivo
        sb.Append(GenerarResumenEjecutivoProyectosHtml(proyectos))

        ' 4. Pendientes Identificados y Contabilizados
        sb.Append(GenerarPendientesProyectosHtml(proyectos))

        ' 5. Antigüedad y Proyectos Destacados
        sb.Append(GenerarAntiguedadProyectosHtml(proyectos))

        ' 6. Análisis Ejecutivo Automático Inteligente
        sb.Append(GenerarAnalisisEjecutivoTextoHtml(proyectos, dtSnapAyer))

        ' 7. Prioridades de Atención Inmediata
        sb.Append(GenerarPrioridadesAtencionHtml(proyectos))

        ' 8. Comparativo Histórico
        sb.Append(GenerarComparativoHistoricoHtml(proyectos, dtSnapAyer, dtSnap7Dias))

        ' 9. Detalle Completo de Proyectos (Agrupado por Clasificación -> Vendedor)
        sb.Append(GenerarDetalleProyectosHtml(proyectos))

        ' 10. Apartado Exclusivo para Proyectos DECLINADOS (activo = 'CERRADO') con Seguimiento Registrado
        sb.Append(GenerarProyectosDeclinadosHtml(proyectos))

        sb.AppendLine("  </div>")

        ' Pie de página institucional
        sb.AppendLine("  <div class=""footer"" style=""background-color: #f8fafc; border-top: 1px solid #e2e8f0; padding: 18px 32px; font-size: 11px; color: #64748b; text-align: center;"">")
        sb.AppendLine("    <p style=""margin: 0 0 4px 0; font-weight: 700; color: #475569;"">HistoMedic LFM RPA Robot &bull; Informe Ejecutivo Diario de Seguimiento de Proyectos</p>")
        sb.AppendLine("    <p style=""margin: 0; color: #64748b;"">Generado automáticamente para el cuerpo directivo y jefes de área. Datos auditados en tiempo real.</p>")
        sb.AppendLine("  </div>")

        sb.AppendLine("</div>")
        sb.AppendLine("</body>")
        sb.AppendLine("</html>")

        Return sb.ToString()
    End Function

    ''' <summary>
    ''' Sección 3: Resumen Ejecutivo con totales, estatus, clasificaciones, vendedores y montos.
    ''' </summary>
    Private Function GenerarResumenEjecutivoProyectosHtml(ByVal proyectos As List(Of ItemProyectoInforme)) As String
        Dim sb As New System.Text.StringBuilder()

        Dim totalActivos As Integer = proyectos.Where(Function(p) Not p.EsCanceladoODeclinado).Count()
        Dim nuevosHoy As Integer = proyectos.Where(Function(p) p.FechaCreacion.Date = DateTime.Now.Date AndAlso Not p.EsCanceladoODeclinado).Count()
        Dim actualizadosHoy As Integer = proyectos.Where(Function(p) p.FechaUltimoMovimiento.Date = DateTime.Now.Date AndAlso Not p.EsCanceladoODeclinado).Count()
        Dim sinMovimiento As Integer = proyectos.Where(Function(p) p.DiasSinMovimiento >= 4 AndAlso Not p.EsCanceladoODeclinado).Count()
        Dim cerradosGanados As Integer = proyectos.Where(Function(p) (p.EstatusId = 6 OrElse p.EstatusId = 7) AndAlso Not p.EsCanceladoODeclinado).Count()
        Dim canceladosDeclinados As Integer = proyectos.Where(Function(p) p.EsCanceladoODeclinado).Count()
        Dim totalCancelados As Integer = proyectos.Where(Function(p) p.EsCancelado).Count()
        Dim totalDeclinados As Integer = proyectos.Where(Function(p) p.EsDeclinado).Count()

        Dim verdesCount As Integer = proyectos.Where(Function(p) p.Semaforo = "VERDE").Count()
        Dim amarillosCount As Integer = proyectos.Where(Function(p) p.Semaforo = "AMARILLO").Count()
        Dim rojosCount As Integer = proyectos.Where(Function(p) p.Semaforo = "ROJO").Count()

        Dim pctV As Double = If(proyectos.Count > 0, Math.Round((CDbl(verdesCount) / CDbl(proyectos.Count)) * 100.0, 1), 0)
        Dim pctA As Double = If(proyectos.Count > 0, Math.Round((CDbl(amarillosCount) / CDbl(proyectos.Count)) * 100.0, 1), 0)
        Dim pctR As Double = If(proyectos.Count > 0, Math.Round((CDbl(rojosCount) / CDbl(proyectos.Count)) * 100.0, 1), 0)

        sb.AppendLine("    <div class=""sec-heading"">&#128202; 1. Resumen Ejecutivo</div>")

        ' Cuadrícula de tarjetas de resumen
        sb.AppendLine("    <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" style=""width: 100%; border-collapse: collapse; margin-bottom: 20px;"">")
        sb.AppendLine("      <tr>")
        sb.AppendLine(String.Format("        <td style=""width: 16.6%; background: #f8fafc; border: 1px solid #cbd5e1; border-radius: 4px; padding: 10px; text-align: center;""><div style=""font-size: 10px; color: #64748b; font-weight: 700; text-transform: uppercase;"">Proyectos Activos</div><div style=""font-size: 16px; font-weight: 800; color: #0f172a;"">{0}</div></td>", totalActivos))
        sb.AppendLine(String.Format("        <td style=""width: 16.6%; background: #f8fafc; border: 1px solid #cbd5e1; border-radius: 4px; padding: 10px; text-align: center;""><div style=""font-size: 10px; color: #64748b; font-weight: 700; text-transform: uppercase;"">Nuevos Hoy</div><div style=""font-size: 16px; font-weight: 800; color: #2563eb;"">{0}</div></td>", nuevosHoy))
        sb.AppendLine(String.Format("        <td style=""width: 16.6%; background: #f8fafc; border: 1px solid #cbd5e1; border-radius: 4px; padding: 10px; text-align: center;""><div style=""font-size: 10px; color: #64748b; font-weight: 700; text-transform: uppercase;"">Actualizados Hoy</div><div style=""font-size: 16px; font-weight: 800; color: #059669;"">{0}</div></td>", actualizadosHoy))
        sb.AppendLine(String.Format("        <td style=""width: 16.6%; background: #f8fafc; border: 1px solid #cbd5e1; border-radius: 4px; padding: 10px; text-align: center;""><div style=""font-size: 10px; color: #64748b; font-weight: 700; text-transform: uppercase;"">Sin Movimiento (&#8805;4d)</div><div style=""font-size: 16px; font-weight: 800; color: #d97706;"">{0}</div></td>", sinMovimiento))
        sb.AppendLine(String.Format("        <td style=""width: 16.6%; background: #f8fafc; border: 1px solid #cbd5e1; border-radius: 4px; padding: 10px; text-align: center;""><div style=""font-size: 10px; color: #64748b; font-weight: 700; text-transform: uppercase;"">En Colocación (OC)</div><div style=""font-size: 16px; font-weight: 800; color: #16a34a;"">{0}</div></td>", cerradosGanados))
        sb.AppendLine(String.Format("        <td style=""width: 16.6%; background: #f8fafc; border: 1px solid #cbd5e1; border-radius: 4px; padding: 10px; text-align: center;""><div style=""font-size: 10px; color: #64748b; font-weight: 700; text-transform: uppercase;"">Cancelados / Declinados</div><div style=""font-size: 16px; font-weight: 800; color: #dc2626;"">{0} <span style=""font-size: 10px; font-weight: normal; color: #991b1b;"">({1} canc. / {2} decl.)</span></div></td>", canceladosDeclinados, totalCancelados, totalDeclinados))
        sb.AppendLine("      </tr>")
        sb.AppendLine("    </table>")

        ' Distribución Semáforo
        sb.AppendLine("    <div style=""margin-bottom: 16px; padding: 12px 16px; background-color: #f1f5f9; border-radius: 6px; font-size: 12px;"">")
        sb.AppendLine(String.Format("      <strong>Estado de Salud del Semáforo:</strong> &nbsp; " & _
                                    "<span class=""badge-v"" style=""display: inline-block; background-color: #dcfce7; color: #15803d !important; border: 1px solid #86efac; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px;"">&#9679; VERDES: {0} ({1}%)</span> &nbsp; " & _
                                    "<span class=""badge-a"" style=""display: inline-block; background-color: #fef9c3; color: #a16207 !important; border: 1px solid #fde047; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px;"">&#9679; AMARILLOS: {2} ({3}%)</span> &nbsp; " & _
                                    "<span class=""badge-r"" style=""display: inline-block; background-color: #fee2e2; color: #b91c1c !important; border: 1px solid #fca5a5; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px;"">&#9679; ROJOS: {4} ({5}%)</span>",
                                    verdesCount, pctV, amarillosCount, pctA, rojosCount, pctR))
        sb.AppendLine("    </div>")

        ' Resumen por Clasificación y por Vendedor en 2 columnas
        sb.AppendLine("    <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" style=""width: 100%; border-collapse: collapse; margin-bottom: 15px;"">")
        sb.AppendLine("      <tr style=""vertical-align: top;"">")

        ' Columna 1: Por Clasificación
        sb.AppendLine("        <td style=""width: 49%; padding-right: 1%;"">")
        sb.AppendLine("          <table class=""data-table"">")
        sb.AppendLine("            <thead>")
        sb.AppendLine("              <tr><th>Clasificación</th><th style=""text-align: center;"">Proy.</th><th style=""text-align: center;"">Declinados</th><th style=""text-align: right;"">Monto USD</th><th style=""text-align: right;"">Monto MXN</th></tr>")
        sb.AppendLine("            </thead>")
        sb.AppendLine("            <tbody>")
        Dim clasifs = proyectos.GroupBy(Function(p) p.ClasificacionNombre).OrderByDescending(Function(g) g.Count)
        For Each g In clasifs
            Dim mtoUSD As Double = g.Where(Function(p) p.MonedaSiglas.Equals("USD", StringComparison.OrdinalIgnoreCase)).Sum(Function(p) p.TotalMonto)
            Dim mtoMXN As Double = g.Where(Function(p) Not p.MonedaSiglas.Equals("USD", StringComparison.OrdinalIgnoreCase)).Sum(Function(p) p.TotalMonto)
            Dim declinadosGrp As Integer = g.Where(Function(p) p.EsDeclinado).Count()
            Dim declinadosStr As String = If(declinadosGrp > 0, String.Format("<span style=""color: #dc2626; font-weight: 700;"">{0}</span>", declinadosGrp), "<span style=""color: #94a3b8;"">0</span>")
            sb.AppendLine(String.Format("              <tr><td><strong>{0}</strong></td><td style=""text-align: center;"">{1}</td><td style=""text-align: center;"">{2}</td><td style=""text-align: right;"">${3:N2}</td><td style=""text-align: right;"">${4:N2}</td></tr>",
                                        System.Net.WebUtility.HtmlEncode(g.Key), g.Count, declinadosStr, mtoUSD, mtoMXN))
        Next
        sb.AppendLine("            </tbody>")
        sb.AppendLine("          </table>")
        sb.AppendLine("        </td>")

        ' Columna 2: Por Vendedor
        sb.AppendLine("        <td style=""width: 49%; padding-left: 1%;"">")
        sb.AppendLine("          <table class=""data-table"">")
        sb.AppendLine("            <thead>")
        sb.AppendLine("              <tr><th>Vendedor</th><th style=""text-align: center;"">Proy.</th><th style=""text-align: center;"">Declinados</th><th style=""text-align: right;"">Monto USD</th><th style=""text-align: right;"">Monto MXN</th></tr>")
        sb.AppendLine("            </thead>")
        sb.AppendLine("            <tbody>")
        Dim vends = proyectos.GroupBy(Function(p) p.VendedorNombre).OrderByDescending(Function(g) g.Count)
        For Each g In vends
            Dim mtoUSD As Double = g.Where(Function(p) p.MonedaSiglas.Equals("USD", StringComparison.OrdinalIgnoreCase)).Sum(Function(p) p.TotalMonto)
            Dim mtoMXN As Double = g.Where(Function(p) Not p.MonedaSiglas.Equals("USD", StringComparison.OrdinalIgnoreCase)).Sum(Function(p) p.TotalMonto)
            Dim declinadosVend As Integer = g.Where(Function(p) p.EsDeclinado).Count()
            Dim declinadosVendStr As String = If(declinadosVend > 0, String.Format("<span style=""color: #dc2626; font-weight: 700;"">{0}</span>", declinadosVend), "<span style=""color: #94a3b8;"">0</span>")
            sb.AppendLine(String.Format("              <tr><td><strong>{0}</strong></td><td style=""text-align: center;"">{1}</td><td style=""text-align: center;"">{2}</td><td style=""text-align: right;"">${3:N2}</td><td style=""text-align: right;"">${4:N2}</td></tr>",
                                        System.Net.WebUtility.HtmlEncode(g.Key), g.Count, declinadosVendStr, mtoUSD, mtoMXN))
        Next
        sb.AppendLine("            </tbody>")
        sb.AppendLine("          </table>")
        sb.AppendLine("        </td>")

        sb.AppendLine("      </tr>")
        sb.AppendLine("    </table>")

        Return sb.ToString()
    End Function

    ''' <summary>
    ''' Sección 4: Contabilización detallada de los 8 tipos de pendientes operativos y comerciales.
    ''' </summary>
    Private Function GenerarPendientesProyectosHtml(ByVal proyectos As List(Of ItemProyectoInforme)) As String
        Dim sb As New System.Text.StringBuilder()

        Dim pendOCCliente As Integer = proyectos.Where(Function(p) p.EstatusId = 5 AndAlso Not p.EsCanceladoODeclinado).Count()
        Dim pendCotInterna As Integer = proyectos.Where(Function(p) p.PendienteCotizacionInterna AndAlso Not p.EsCanceladoODeclinado).Count()
        Dim procesoCotProv As Integer = proyectos.Where(Function(p) ((p.EstatusId = 1 AndAlso p.Enviada = 1) OrElse (p.EstatusId = 3 AndAlso Not p.PendienteCotizacionInterna)) AndAlso Not p.EsCanceladoODeclinado).Count()
        Dim pendEnviarCotCli As Integer = proyectos.Where(Function(p) p.EstatusId = 4 AndAlso Not p.EsCanceladoODeclinado).Count()
        Dim pendRespCliente As Integer = proyectos.Where(Function(p) p.EstatusId = 5 AndAlso p.TotalCotizacionesCliente > 0 AndAlso Not p.EsCanceladoODeclinado).Count()
        Dim pendInfo As Integer = proyectos.Where(Function(p) p.EstatusId = 1 AndAlso p.Enviada = 0 AndAlso Not p.EsCanceladoODeclinado).Count()
        Dim fchCompVencida As Integer = proyectos.Where(Function(p) p.FechaCompromiso.HasValue AndAlso p.DiasParaCompromiso.HasValue AndAlso p.DiasParaCompromiso.Value < 0 AndAlso Not p.EsCanceladoODeclinado).Count()
        Dim sinMov4d As Integer = proyectos.Where(Function(p) p.DiasSinMovimiento >= 4 AndAlso Not p.EsCanceladoODeclinado).Count()

        sb.AppendLine("    <div class=""sec-heading"">&#9888;&#65039; 2. Balance y Conteo de Pendientes</div>")
        sb.AppendLine("    <table class=""data-table"">")
        sb.AppendLine("      <thead>")
        sb.AppendLine("        <tr><th>Categoría de Pendiente Detectado</th><th style=""text-align: center;"">Proyectos</th><th>Impacto Operativo / Comercial</th><th style=""text-align: center;"">Acción Requerida</th></tr>")
        sb.AppendLine("      </thead>")
        sb.AppendLine("      <tbody>")
        sb.AppendLine(String.Format("        <tr><td><strong>Pendientes de Orden de Compra del Cliente</strong></td><td style=""text-align: center; font-weight: bold; color: #b45309;"">{0}</td><td>Cotizaciones en poder del cliente sin decisión formal de compra</td><td style=""text-align: center;"">Cierre comercial</td></tr>", pendOCCliente))
        sb.AppendLine(String.Format("        <tr><td><strong>Pendientes de Elaborar Cotización Interna</strong></td><td style=""text-align: center; font-weight: bold; color: #b45309;"">{0}</td><td>Proyectos cotizados que no se ha enviado cotización interna</td><td style=""text-align: center;"">Departamento de Compras</td></tr>", pendCotInterna))
        sb.AppendLine(String.Format("        <tr><td><strong>En Proceso de Cotización de Proveedor</strong></td><td style=""text-align: center; font-weight: bold;"">{0}</td><td>Solicitudes enviadas a fabricantes en espera de precio y tiempo entrega</td><td style=""text-align: center;"">Seguimiento compras</td></tr>", procesoCotProv))
        sb.AppendLine(String.Format("        <tr><td><strong>Pendientes de Enviar Cotización al Cliente</strong></td><td style=""text-align: center; font-weight: bold; color: #b91c1c;"">{0}</td><td>Cotización interna lista, pendiente vendedor elabore cotización a cliente.</td><td style=""text-align: center;"">Envío inmediato</td></tr>", pendEnviarCotCli))
        sb.AppendLine(String.Format("        <tr><td><strong>Pendientes de Respuesta / Confirmación del Cliente</strong></td><td style=""text-align: center; font-weight: bold;"">{0}</td><td>Propuesta técnica-económica entregada al cliente</td><td style=""text-align: center;"">Llamada de seguimiento</td></tr>", pendRespCliente))
        sb.AppendLine(String.Format("        <tr><td><strong>Pendientes de Notificar a Compras</strong></td><td style=""text-align: center; font-weight: bold;"">{0}</td><td>Oportunidades en fase inicial que el vendedor aun no ha notificado a Compras</td><td style=""text-align: center;"">Levantamiento de datos</td></tr>", pendInfo))
        sb.AppendLine(String.Format("        <tr style=""background-color: #fef2f2;""><td><strong style=""color: #b91c1c;"">Proyectos con Fecha Compromiso Vencida</strong></td><td style=""text-align: center; font-weight: 800; color: #b91c1c;"">{0}</td><td style=""color: #991b1b;"">Compromiso de entrega o vigencia superado; alto riesgo de penalización o pérdida</td><td style=""text-align: center; font-weight: bold; color: #b91c1c;"">Intervención Urgente</td></tr>", fchCompVencida))
        sb.AppendLine(String.Format("        <tr><td><strong>Proyectos Sin Movimiento (&#8805; 4 días)</strong></td><td style=""text-align: center; font-weight: bold; color: #d97706;"">{0}</td><td>Proyectos estancados que requieren reactivación y actualización en bitácora</td><td style=""text-align: center;"">Revisión de estatus</td></tr>", sinMov4d))
        sb.AppendLine("      </tbody>")
        sb.AppendLine("    </table>")

        Return sb.ToString()
    End Function

    ''' <summary>
    ''' Sección 5: Clasificación de proyectos por antigüedad de inactividad.
    ''' </summary>
    Private Function GenerarAntiguedadProyectosHtml(ByVal proyectos As List(Of ItemProyectoInforme)) As String
        Dim sb As New System.Text.StringBuilder()

        Dim r0_3 As Integer = proyectos.Where(Function(p) p.DiasSinMovimiento <= 3).Count()
        Dim r4_7 As Integer = proyectos.Where(Function(p) p.DiasSinMovimiento >= 4 AndAlso p.DiasSinMovimiento <= 7).Count()
        Dim r8_15 As Integer = proyectos.Where(Function(p) p.DiasSinMovimiento >= 8 AndAlso p.DiasSinMovimiento <= 15).Count()
        Dim r16mas As Integer = proyectos.Where(Function(p) p.DiasSinMovimiento > 15).Count()

        Dim proyMasAntiguo = proyectos.OrderByDescending(Function(p) p.DiasSinMovimiento).FirstOrDefault()
        Dim proyMayorMonto = proyectos.OrderByDescending(Function(p) p.TotalMonto).FirstOrDefault()

        sb.AppendLine("    <div class=""sec-heading"">&#9201; 3. Análisis de Antigüedad de Inactividad</div>")
        sb.AppendLine("    <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" style=""width: 100%; border-collapse: collapse; margin-bottom: 12px;"">")
        sb.AppendLine("      <tr>")
        sb.AppendLine(String.Format("        <td style=""width: 25%; background: #f0fdf4; border: 1px solid #bbf7d0; border-radius: 4px; padding: 10px; text-align: center;""><div style=""font-size: 11px; font-weight: 700; color: #15803d;"">0 a 3 Días (Al día)</div><div style=""font-size: 18px; font-weight: 800; color: #16a34a;"">{0} proy.</div></td>", r0_3))
        sb.AppendLine(String.Format("        <td style=""width: 25%; background: #fefce8; border: 1px solid #fef08a; border-radius: 4px; padding: 10px; text-align: center;""><div style=""font-size: 11px; font-weight: 700; color: #854d0e;"">4 a 7 Días (Seguimiento)</div><div style=""font-size: 18px; font-weight: 800; color: #ca8a04;"">{0} proy.</div></td>", r4_7))
        sb.AppendLine(String.Format("        <td style=""width: 25%; background: #fff7ed; border: 1px solid #fed7aa; border-radius: 4px; padding: 10px; text-align: center;""><div style=""font-size: 11px; font-weight: 700; color: #9a3412;"">8 a 15 Días (Atención)</div><div style=""font-size: 18px; font-weight: 800; color: #ea580c;"">{0} proy.</div></td>", r8_15))
        sb.AppendLine(String.Format("        <td style=""width: 25%; background: #fef2f2; border: 1px solid #fecaca; border-radius: 4px; padding: 10px; text-align: center;""><div style=""font-size: 11px; font-weight: 700; color: #991b1b;"">Más de 15 Días (Crítico)</div><div style=""font-size: 18px; font-weight: 800; color: #dc2626;"">{0} proy.</div></td>", r16mas))
        sb.AppendLine("      </tr>")
        sb.AppendLine("    </table>")

        ' Destacar Proyectos con mayor antigüedad y mayor monto
        sb.AppendLine("    <div style=""font-size: 11px; color: #475569; margin-bottom: 20px;"">")
        If proyMasAntiguo IsNot Nothing Then
            sb.AppendLine(String.Format("      &bull; <strong>Mayor inactividad registrada:</strong> Folio <span style=""color: #0f172a; font-weight: 700;"">{0}</span> ({1} - {2}) con <strong>{3} días</strong> sin actualización.<br/>",
                                        proyMasAntiguo.ProyectoId, System.Net.WebUtility.HtmlEncode(proyMasAntiguo.ClienteNombre), System.Net.WebUtility.HtmlEncode(proyMasAntiguo.VendedorNombre), proyMasAntiguo.DiasSinMovimiento))
        End If
        If proyMayorMonto IsNot Nothing Then
            sb.AppendLine(String.Format("      &bull; <strong>Mayor valor económico en cartera:</strong> Folio <span style=""color: #0f172a; font-weight: 700;"">{0}</span> ({1}) por <strong>{2:C2} {3}</strong> &bull; Estatus: {4} &bull; Semáforo: {5}.",
                                        proyMayorMonto.ProyectoId, System.Net.WebUtility.HtmlEncode(proyMayorMonto.ClienteNombre), proyMayorMonto.TotalMonto, proyMayorMonto.MonedaSiglas, proyMayorMonto.EstatusNombre, proyMayorMonto.Semaforo))
        End If
        sb.AppendLine("    </div>")

        Return sb.ToString()
    End Function

    ''' <summary>
    ''' Sección 6: Análisis Ejecutivo Inteligente estructurado para directores y jefes de área.
    ''' </summary>
    Private Function GenerarAnalisisEjecutivoTextoHtml(ByVal proyectos As List(Of ItemProyectoInforme), ByVal dtSnapAyer As DataTable) As String
        Dim sb As New System.Text.StringBuilder()

        Dim totalP As Integer = proyectos.Count
        Dim totalActivos As Integer = proyectos.Where(Function(p) Not p.EsCanceladoODeclinado).Count()
        Dim totalCerrados As Integer = proyectos.Where(Function(p) p.EsCanceladoODeclinado).Count()
        Dim totalDeclinados As Integer = proyectos.Where(Function(p) p.EsDeclinado).Count()
        Dim totalCancelados As Integer = proyectos.Where(Function(p) p.EsCancelado).Count()

        Dim rojos = proyectos.Where(Function(p) p.Semaforo = "ROJO" AndAlso Not p.EsCanceladoODeclinado).OrderByDescending(Function(p) p.TotalMonto).ToList()
        Dim amarillos = proyectos.Where(Function(p) p.Semaforo = "AMARILLO" AndAlso Not p.EsCanceladoODeclinado).ToList()
        Dim verdes = proyectos.Where(Function(p) p.Semaforo = "VERDE" AndAlso Not p.EsCanceladoODeclinado).ToList()

        ' Vendedor con mayor concentración de proyectos en ROJO o AMARILLO (activos)
        Dim vendedoresCriticos = proyectos.Where(Function(p) (p.Semaforo = "ROJO" OrElse p.Semaforo = "AMARILLO") AndAlso Not p.EsCanceladoODeclinado) _
                                          .GroupBy(Function(p) p.VendedorNombre) _
                                          .OrderByDescending(Function(g) g.Count) _
                                          .ToList()
        Dim topVendCriticoNombre As String = If(vendedoresCriticos.Count > 0, vendedoresCriticos(0).Key, "N/A")
        Dim topVendCriticoCount As Integer = If(vendedoresCriticos.Count > 0, vendedoresCriticos(0).Count, 0)

        ' Proyectos de mayor impacto económico en riesgo (ROJO)
        Dim topRojoMonto = rojos.FirstOrDefault()

        ' Transiciones de semáforo (de VERDE ayer a AMARILLO/ROJO hoy)
        Dim degradados As New List(Of String)()
        If dtSnapAyer IsNot Nothing AndAlso dtSnapAyer.Rows.Count > 0 Then
            For Each p In proyectos.Where(Function(item) Not item.EsCanceladoODeclinado)
                If p.Semaforo = "AMARILLO" OrElse p.Semaforo = "ROJO" Then
                    Dim rowsAyer = dtSnapAyer.Select(String.Format("proyecto_id = '{0}'", p.ProyectoId.Replace("'", "''")))
                    If rowsAyer.Length > 0 Then
                        Dim semAyer As String = rowsAyer(0)("semaforo").ToString().Trim()
                        If semAyer.Equals("VERDE", StringComparison.OrdinalIgnoreCase) Then
                            degradados.Add(String.Format("{0} ({1} &rarr; {2})", p.ProyectoId, semAyer, p.Semaforo))
                        End If
                    End If
                End If
            Next
        End If

        sb.AppendLine("    <div class=""sec-heading"">&#128065; 4. Análisis Ejecutivo Inteligente</div>")
        sb.AppendLine("    <div class=""analisis-box"">")
        sb.AppendLine("      <div style=""font-weight: 700; font-size: 13px; color: #166534; margin-bottom: 6px;"">&#9658; Diagnóstico General y Comportamiento del Semáforo:</div>")
        sb.AppendLine("      <ul>")

        ' Diagnóstico 1: Balance general de cartera activa
        Dim pctRojos As Double = If(totalActivos > 0, Math.Round((CDbl(rojos.Count) / CDbl(totalActivos)) * 100.0, 1), 0)
        Dim pctVerdes As Double = If(totalActivos > 0, Math.Round((CDbl(verdes.Count) / CDbl(totalActivos)) * 100.0, 1), 0)
        sb.AppendLine(String.Format("        <li><strong>Estado de la Cartera Activa:</strong> De un universo total de <strong>{0} proyectos</strong> registrados desde el 24/08/2026, <strong>{1} se encuentran en seguimiento comercial activo</strong> ({2} proyectos fuera de flujo: {3} cancelados y {4} declinado). El <strong>{5}% ({6} proyectos)</strong> opera en condiciones óptimas (VERDE), mientras que el <strong>{7}% ({8} proyectos)</strong> se encuentra en semáforo ROJO requiriendo acción resolutiva.</li>",
                                    totalP, totalActivos, totalCerrados, totalCancelados, totalDeclinados, pctVerdes, verdes.Count, pctRojos, rojos.Count))

        ' Diagnóstico 2: Proyectos críticos activos en ROJO y motivos
        If rojos.Count > 0 Then
            Dim foliosTopRojos As String = String.Join(", ", rojos.Take(4).Select(Function(p) p.ProyectoId & " (" & p.MotivoPrioridad & ")"))
            sb.AppendLine(String.Format("        <li><strong>Foco Rojo Operativo:</strong> Los proyectos prioritarios con rezago o vencimiento son: {0}.</li>", foliosTopRojos))
        Else
            sb.AppendLine("        <li><strong>Sin Focos Rojos Operativos:</strong> No se detectaron proyectos activos con retrasos críticos o compromisos vencidos.</li>")
        End If

        ' Diagnóstico 3: Mayor impacto económico en riesgo
        If topRojoMonto IsNot Nothing AndAlso topRojoMonto.TotalMonto > 0 Then
            sb.AppendLine(String.Format("        <li><strong>Riesgo Económico:</strong> El proyecto con mayor capital comprometido en situación de riesgo es <strong>{0}</strong> ({1}) con un importe de <strong>{2:C2} {3}</strong> a cargo de <em>{4}</em>.</li>",
                                        topRojoMonto.ProyectoId, System.Net.WebUtility.HtmlEncode(topRojoMonto.ClienteNombre), topRojoMonto.TotalMonto, topRojoMonto.MonedaSiglas, System.Net.WebUtility.HtmlEncode(topRojoMonto.VendedorNombre)))
        End If

        ' Diagnóstico 4: Concentración por Vendedor
        If topVendCriticoCount > 0 Then
            sb.AppendLine(String.Format("        <li><strong>Concentración de Pendientes:</strong> El ejecutivo <strong>{0}</strong> concentra <strong>{1} proyectos</strong> con necesidad de atención inmediata o seguimiento preventivo. Se recomienda coordinar reunión de desahogo comercial.</li>",
                                        System.Net.WebUtility.HtmlEncode(topVendCriticoNombre), topVendCriticoCount))
        End If

        ' Diagnóstico 5: Alerta de transiciones respecto a ayer
        If degradados.Count > 0 Then
            sb.AppendLine(String.Format("        <li><strong style=""color: #b91c1c;"">Alerta de Deterioro de Semáforo:</strong> {0} proyecto(s) pasaron de VERDE a condición de advertencia: <strong>{1}</strong>.</li>",
                                        degradados.Count, String.Join(", ", degradados)))
        ElseIf dtSnapAyer IsNot Nothing AndAlso dtSnapAyer.Rows.Count > 0 Then
            sb.AppendLine("        <li><strong>Estabilidad del Semáforo:</strong> No se presentaron degradaciones de proyectos de VERDE a semáforo restrictivo respecto a la jornada previa.</li>")
        End If

        ' Diagnóstico 6: Apartado de Declinados
        If totalDeclinados > 0 Then
            sb.AppendLine(String.Format("        <li><strong>Proyectos Declinados ({0}):</strong> Se ha segregado en el <em>Apartado 8</em> el detalle del proyecto declinado con su bitácora de seguimiento completa para consulta y justificación comercial.</li>", totalDeclinados))
        End If

        ' Diagnóstico 7: Recomendación Directiva
        'sb.AppendLine("        <li><strong>Intervención Directiva Sugerida:</strong> Agilizar la confirmación de cotizaciones pendientes de orden de compra con clientes.</li>")

        sb.AppendLine("      </ul>")
        sb.AppendLine("    </div>")

        Return sb.ToString()
    End Function

    ''' <summary>
    ''' Sección 7: Prioridades de Atención con ordenamiento estricto por criticidad.
    ''' </summary>
    Private Function GenerarPrioridadesAtencionHtml(ByVal proyectos As List(Of ItemProyectoInforme)) As String
        Dim sb As New System.Text.StringBuilder()

        ' Ordenamiento por prioridad (solo proyectos con orden de compra de cliente pendiente: EstatusId = 5):
        ' 1. Semáforo ROJO primero, luego AMARILLO, luego VERDE
        ' 2. Compromiso vencido o próximo
        ' 3. Días sin movimiento descendente
        ' 4. Monto económico descendente
        Dim prioritarios = proyectos.Where(Function(p) p.EstatusId = 5 AndAlso Not p.EsCanceladoODeclinado) _
                                    .OrderBy(Function(p) If(p.Semaforo = "ROJO", 0, If(p.Semaforo = "AMARILLO", 1, 2))) _
                                    .ThenBy(Function(p) If(p.DiasParaCompromiso.HasValue, p.DiasParaCompromiso.Value, 9999)) _
                                    .ThenByDescending(Function(p) p.DiasSinMovimiento) _
                                    .ThenByDescending(Function(p) p.TotalMonto) _
                                    .ToList()

        sb.AppendLine("    <div class=""sec-heading"">&#127919; 5. Prioridades de Atención Inmediata</div>")
        sb.AppendLine("    <p style=""font-size: 12px; color: #64748b; margin: -6px 0 14px 0;"">Listado clasificado de proyectos activos que demandan acción ejecutiva inmediata y seguimiento prioritario.</p>")

        For Each p In prioritarios
            Dim badgeClass As String = If(p.Semaforo = "ROJO", "badge-r", If(p.Semaforo = "AMARILLO", "badge-a", "badge-v"))
            Dim badgeStyle As String = If(p.Semaforo = "ROJO",
                "display: inline-block; background-color: #fee2e2; color: #b91c1c !important; border: 1px solid #fca5a5; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px;",
                If(p.Semaforo = "AMARILLO",
                    "display: inline-block; background-color: #fef9c3; color: #a16207 !important; border: 1px solid #fde047; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px;",
                    "display: inline-block; background-color: #dcfce7; color: #15803d !important; border: 1px solid #86efac; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px;"))
            Dim borderCol As String = If(p.Semaforo = "ROJO", "#dc2626", If(p.Semaforo = "AMARILLO", "#d97706", "#16a34a"))
            Dim bgCol As String = If(p.Semaforo = "ROJO", "#fff1f2", If(p.Semaforo = "AMARILLO", "#fffbeb", "#f0fdf4"))

            sb.AppendLine(String.Format("    <div class=""card-prio"" style=""border-left-color: {0}; background-color: {1};"">", borderCol, bgCol))
            sb.AppendLine("      <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" style=""width: 100%; border-collapse: collapse;"">")
            sb.AppendLine("        <tr>")
            sb.AppendLine(String.Format("          <td style=""font-size: 13px; font-weight: 700; color: #0f172a;""><span class=""{0}"" style=""{1}"">&#9679; {2}</span> &nbsp; Folio: <span style=""font-family: Consolas, monospace;"">{3}</span> &bull; {4}</td>",
                                        badgeClass, badgeStyle, p.Semaforo, p.ProyectoId, System.Net.WebUtility.HtmlEncode(p.ClienteNombre)))
            sb.AppendLine(String.Format("          <td style=""text-align: right; font-weight: 800; font-size: 13px; color: #0f172a;"">{0:C2} {1}</td>", p.TotalMonto, p.MonedaSiglas))
            sb.AppendLine("        </tr>")
            sb.AppendLine("      </table>")
            sb.AppendLine(String.Format("      <div style=""font-size: 12px; color: #334155; margin: 5px 0;""><strong>Descripción:</strong> {0}</div>", System.Net.WebUtility.HtmlEncode(p.Titulo)))
            sb.AppendLine("      <div class=""card-prio-meta"">")
            sb.AppendLine(String.Format("        &bull; <strong>Qué está pendiente:</strong> <span style=""color: #991b1b; font-weight: 600;"">{0}</span><br/>", System.Net.WebUtility.HtmlEncode(p.MotivoPrioridad)))
            sb.AppendLine(String.Format("        &bull; <strong>Quién es responsable:</strong> <span style=""color: #0f172a; font-weight: 600;"">{0}</span><br/>", System.Net.WebUtility.HtmlEncode(p.Responsable)))
            sb.AppendLine(String.Format("        &bull; <strong>Acción recomendada:</strong> <span style=""color: #1e3a8a; font-weight: 600;"">{0}</span>", System.Net.WebUtility.HtmlEncode(p.ProximaAccion)))
            sb.AppendLine("      </div>")
            sb.AppendLine("    </div>")
        Next

        Return sb.ToString()
    End Function

    ''' <summary>
    ''' Sección 8: Comparativo Histórico contra el día anterior y los últimos 7 días.
    ''' </summary>
    Private Function GenerarComparativoHistoricoHtml(ByVal proyectos As List(Of ItemProyectoInforme),
                                                     ByVal dtAyer As DataTable,
                                                     ByVal dt7Dias As DataTable) As String
        Dim sb As New System.Text.StringBuilder()

        sb.AppendLine("    <div class=""sec-heading"">&#128200; 6. Comparativo Histórico de Evolución</div>")

        If (dtAyer Is Nothing OrElse dtAyer.Rows.Count = 0) AndAlso (dt7Dias Is Nothing OrElse dt7Dias.Rows.Count = 0) Then
            sb.AppendLine("    <div style=""padding: 14px 18px; background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 6px; font-size: 12px; color: #475569; margin-bottom: 20px;"">")
            sb.AppendLine("      &#8505;&#65039; <strong>Primer ciclo de registro histórico del informe.</strong> El comparativo de variación diaria y semanal comenzará a proyectarse automáticamente a partir de la próxima ejecución matutina.")
            sb.AppendLine("    </div>")
            Return sb.ToString()
        End If

        ' Métricas actuales
        Dim actHoy As Integer = proyectos.Where(Function(p) p.EstatusId <> 2).Count()
        Dim nuevHoy As Integer = proyectos.Where(Function(p) p.FechaCreacion.Date = DateTime.Now.Date).Count()
        Dim atrasHoy As Integer = proyectos.Where(Function(p) p.Semaforo = "ROJO").Count()
        Dim sinMovHoy As Integer = proyectos.Where(Function(p) p.DiasSinMovimiento >= 4).Count()
        Dim mtoUSDHoy As Double = proyectos.Where(Function(p) p.MonedaSiglas.Equals("USD", StringComparison.OrdinalIgnoreCase)).Sum(Function(p) p.TotalMonto)
        Dim vHoy As Integer = proyectos.Where(Function(p) p.Semaforo = "VERDE").Count()
        Dim aHoy As Integer = proyectos.Where(Function(p) p.Semaforo = "AMARILLO").Count()
        Dim rHoy As Integer = proyectos.Where(Function(p) p.Semaforo = "ROJO").Count()

        ' Métricas ayer
        Dim actAyer As Integer = If(dtAyer IsNot Nothing, dtAyer.Select("estatus_proyecto_id <> 2").Length, 0)
        Dim atrasAyer As Integer = If(dtAyer IsNot Nothing, dtAyer.Select("semaforo = 'ROJO'").Length, 0)
        Dim sinMovAyer As Integer = If(dtAyer IsNot Nothing, dtAyer.Select("dias_sin_movimiento >= 4").Length, 0)
        Dim vAyer As Integer = If(dtAyer IsNot Nothing, dtAyer.Select("semaforo = 'VERDE'").Length, 0)
        Dim aAyer As Integer = If(dtAyer IsNot Nothing, dtAyer.Select("semaforo = 'AMARILLO'").Length, 0)
        Dim rAyer As Integer = If(dtAyer IsNot Nothing, dtAyer.Select("semaforo = 'ROJO'").Length, 0)

        ' Métricas 7 días
        Dim act7D As Integer = If(dt7Dias IsNot Nothing, dt7Dias.Select("estatus_proyecto_id <> 2").Length, 0)
        Dim atras7D As Integer = If(dt7Dias IsNot Nothing, dt7Dias.Select("semaforo = 'ROJO'").Length, 0)
        Dim sinMov7D As Integer = If(dt7Dias IsNot Nothing, dt7Dias.Select("dias_sin_movimiento >= 4").Length, 0)
        Dim v7D As Integer = If(dt7Dias IsNot Nothing, dt7Dias.Select("semaforo = 'VERDE'").Length, 0)
        Dim a7D As Integer = If(dt7Dias IsNot Nothing, dt7Dias.Select("semaforo = 'AMARILLO'").Length, 0)
        Dim r7D As Integer = If(dt7Dias IsNot Nothing, dt7Dias.Select("semaforo = 'ROJO'").Length, 0)

        sb.AppendLine("    <table class=""data-table"">")
        sb.AppendLine("      <thead>")
        sb.AppendLine("        <tr><th>Indicador Ejecutivo</th><th style=""text-align: center;"">Hoy</th><th style=""text-align: center;"">Día Anterior</th><th style=""text-align: center;"">Hace 7 Días</th><th style=""text-align: center;"">Tendencia</th></tr>")
        sb.AppendLine("      </thead>")
        sb.AppendLine("      <tbody>")
        sb.AppendLine(String.Format("        <tr><td><strong>Proyectos Activos</strong></td><td style=""text-align: center; font-weight: 700;"">{0}</td><td style=""text-align: center;"">{1}</td><td style=""text-align: center;"">{2}</td><td style=""text-align: center;"">{3}</td></tr>",
                                    actHoy, If(dtAyer IsNot Nothing, actAyer.ToString(), "-"), If(dt7Dias IsNot Nothing, act7D.ToString(), "-"), FormatearTendencia(actHoy, actAyer)))
        sb.AppendLine(String.Format("        <tr><td><strong>Proyectos Atrasados / Críticos (ROJO)</strong></td><td style=""text-align: center; font-weight: 700; color: #b91c1c;"">{0}</td><td style=""text-align: center;"">{1}</td><td style=""text-align: center;"">{2}</td><td style=""text-align: center;"">{3}</td></tr>",
                                    atrasHoy, If(dtAyer IsNot Nothing, atrasAyer.ToString(), "-"), If(dt7Dias IsNot Nothing, atras7D.ToString(), "-"), FormatearTendenciaInversa(atrasHoy, atrasAyer)))
        sb.AppendLine(String.Format("        <tr><td><strong>Proyectos Sin Movimiento (&#8805; 4 días)</strong></td><td style=""text-align: center; font-weight: 700;"">{0}</td><td style=""text-align: center;"">{1}</td><td style=""text-align: center;"">{2}</td><td style=""text-align: center;"">{3}</td></tr>",
                                    sinMovHoy, If(dtAyer IsNot Nothing, sinMovAyer.ToString(), "-"), If(dt7Dias IsNot Nothing, sinMov7D.ToString(), "-"), FormatearTendenciaInversa(sinMovHoy, sinMovAyer)))
        sb.AppendLine(String.Format("        <tr><td><strong>Evolución Semáforo VERDE</strong></td><td style=""text-align: center; font-weight: 700; color: #15803d;"">{0}</td><td style=""text-align: center;"">{1}</td><td style=""text-align: center;"">{2}</td><td style=""text-align: center;"">{3}</td></tr>",
                                    vHoy, If(dtAyer IsNot Nothing, vAyer.ToString(), "-"), If(dt7Dias IsNot Nothing, v7D.ToString(), "-"), FormatearTendencia(vHoy, vAyer)))
        sb.AppendLine(String.Format("        <tr><td><strong>Evolución Semáforo AMARILLO</strong></td><td style=""text-align: center; font-weight: 700; color: #a16207;"">{0}</td><td style=""text-align: center;"">{1}</td><td style=""text-align: center;"">{2}</td><td style=""text-align: center;"">{3}</td></tr>",
                                    aHoy, If(dtAyer IsNot Nothing, aAyer.ToString(), "-"), If(dt7Dias IsNot Nothing, a7D.ToString(), "-"), "-"))
        sb.AppendLine(String.Format("        <tr><td><strong>Evolución Semáforo ROJO</strong></td><td style=""text-align: center; font-weight: 700; color: #b91c1c;"">{0}</td><td style=""text-align: center;"">{1}</td><td style=""text-align: center;"">{2}</td><td style=""text-align: center;"">{3}</td></tr>",
                                    rHoy, If(dtAyer IsNot Nothing, rAyer.ToString(), "-"), If(dt7Dias IsNot Nothing, r7D.ToString(), "-"), FormatearTendenciaInversa(rHoy, rAyer)))
        sb.AppendLine("      </tbody>")
        sb.AppendLine("    </table>")

        Return sb.ToString()
    End Function

    Private Function FormatearTendencia(ByVal actual As Integer, ByVal anterior As Integer) As String
        If anterior = 0 Then Return "-"
        Dim diff As Integer = actual - anterior
        If diff > 0 Then Return String.Format("<span style=""color: #16a34a; font-weight: bold;"">&#9650; +{0}</span>", diff)
        If diff < 0 Then Return String.Format("<span style=""color: #dc2626; font-weight: bold;"">&#9660; {0}</span>", diff)
        Return "<span style=""color: #64748b;"">&#9644; 0</span>"
    End Function

    Private Function FormatearTendenciaInversa(ByVal actual As Integer, ByVal anterior As Integer) As String
        If anterior = 0 Then Return "-"
        Dim diff As Integer = actual - anterior
        If diff > 0 Then Return String.Format("<span style=""color: #dc2626; font-weight: bold;"">&#9650; +{0}</span>", diff)
        If diff < 0 Then Return String.Format("<span style=""color: #16a34a; font-weight: bold;"">&#9660; {0}</span>", diff)
        Return "<span style=""color: #64748b;"">&#9644; 0</span>"
    End Function

    ''' <summary>
    ''' Sección 1 y 9: Detalle Estructurado de Proyectos agrupados por Clasificación -> Vendedor -> Fila de Proyecto.
    ''' </summary>
    Private Function GenerarDetalleProyectosHtml(ByVal proyectos As List(Of ItemProyectoInforme)) As String
        Dim sb As New System.Text.StringBuilder()

        sb.AppendLine("    <div class=""sec-heading"">&#128221; 7. Detalle Estructurado por Clasificación y Vendedor</div>")

        Dim proyectosParaDetalle = proyectos.Where(Function(p) Not p.EsCanceladoODeclinado).ToList()
        Dim clasificaciones = proyectosParaDetalle.GroupBy(Function(p) p.ClasificacionNombre).OrderBy(Function(g) g.Key)

        For Each grpClasif In clasificaciones
            Dim totalPryClasif As Integer = grpClasif.Count
            Dim mtoUSDClasif As Double = grpClasif.Where(Function(p) p.MonedaSiglas.Equals("USD", StringComparison.OrdinalIgnoreCase)).Sum(Function(p) p.TotalMonto)
            Dim mtoMXNClasif As Double = grpClasif.Where(Function(p) Not p.MonedaSiglas.Equals("USD", StringComparison.OrdinalIgnoreCase)).Sum(Function(p) p.TotalMonto)

            sb.AppendLine("    <div class=""clasif-block"" style=""margin-bottom: 26px; border: 1px solid #cbd5e1; border-radius: 6px; overflow: hidden;"">")
            sb.AppendLine(String.Format("      <div class=""clasif-bar"" style=""background-color: #1e3a8a; color: #ffffff !important; padding: 10px 16px; font-weight: 700; font-size: 14px; text-transform: uppercase; letter-spacing: 0.5px;"">&#9658; Clasificación: {0} ({1} proyectos &bull; ${2:N0} USD &bull; ${3:N0} MXN)</div>",
                                        System.Net.WebUtility.HtmlEncode(grpClasif.Key), totalPryClasif, mtoUSDClasif, mtoMXNClasif))

            ' Subagrupar por Vendedor
            Dim vendedores = grpClasif.GroupBy(Function(p) p.VendedorNombre).OrderBy(Function(g) g.Key)

            For Each grpVend In vendedores
                sb.AppendLine(String.Format("      <div class=""vendedor-bar"" style=""background-color: #e2e8f0; color: #0f172a !important; padding: 7px 16px; font-weight: 700; font-size: 12px; border-top: 1px solid #cbd5e1; border-bottom: 1px solid #cbd5e1;"">&#128100; Vendedor: {0} ({1} proyectos)</div>",
                                            System.Net.WebUtility.HtmlEncode(grpVend.Key), grpVend.Count))

                sb.AppendLine("      <table class=""data-table"" style=""margin-bottom: 0;"">")
                sb.AppendLine("        <thead>")
                sb.AppendLine("          <tr>")
                sb.AppendLine("            <th style=""width: 9%;"">Folio</th>")
                sb.AppendLine("            <th style=""width: 15%;"">Cliente</th>")
                sb.AppendLine("            <th style=""width: 20%;"">Descripción</th>")
                sb.AppendLine("            <th style=""width: 11%;"">Estatus</th>")
                sb.AppendLine("            <th style=""width: 8%; text-align: center;"">Últ. Mov.</th>")
                sb.AppendLine("            <th style=""width: 5%; text-align: center;"">Inact.</th>")
                sb.AppendLine("            <th style=""width: 9%; text-align: right;"">Monto</th>")
                sb.AppendLine("            <th style=""width: 13%;"">Próxima Acción / Resp.</th>")
                sb.AppendLine("            <th style=""width: 5%; text-align: center;"">Compromiso</th>")
                sb.AppendLine("            <th style=""width: 5%; text-align: center;"">Semáforo</th>")
                sb.AppendLine("          </tr>")
                sb.AppendLine("        </thead>")
                sb.AppendLine("        <tbody>")

                For Each p In grpVend
                    Dim badgeCls As String = If(p.Semaforo = "ROJO", "badge-r", If(p.Semaforo = "AMARILLO", "badge-a", "badge-v"))
                    Dim badgeStyle As String = If(p.Semaforo = "ROJO",
                        "display: inline-block; background-color: #fee2e2; color: #b91c1c !important; border: 1px solid #fca5a5; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px;",
                        If(p.Semaforo = "AMARILLO",
                            "display: inline-block; background-color: #fef9c3; color: #a16207 !important; border: 1px solid #fde047; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px;",
                            "display: inline-block; background-color: #dcfce7; color: #15803d !important; border: 1px solid #86efac; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px;"))
                    Dim fchCompStr As String = "-"
                    If p.FechaCompromiso.HasValue Then
                        fchCompStr = String.Format("<span title=""{0}"">{1:dd/MM/yy}</span>", p.TipoFechaCompromiso, p.FechaCompromiso.Value)
                        If p.DiasParaCompromiso.HasValue AndAlso p.DiasParaCompromiso.Value < 0 Then
                            fchCompStr &= String.Format("<br/><span style=""color: #b91c1c; font-size: 10px; font-weight: bold;"">({0}d)</span>", p.DiasParaCompromiso.Value)
                        End If
                    End If

                    Dim clienteStr As String = System.Net.WebUtility.HtmlEncode(p.ClienteNombre)
                    If Not String.IsNullOrWhiteSpace(p.ClienteFinal) AndAlso Not p.ClienteFinal.Equals(p.ClienteNombre, StringComparison.OrdinalIgnoreCase) Then
                        clienteStr &= String.Format("<br/><span style=""font-size: 10px; color: #64748b;"">Final: {0}</span>", System.Net.WebUtility.HtmlEncode(p.ClienteFinal))
                    End If

                    sb.AppendLine("          <tr>")
                    sb.AppendLine(String.Format("            <td><strong style=""font-family: Consolas, monospace; color: #0f172a;"">{0}</strong></td>", p.ProyectoId))
                    sb.AppendLine(String.Format("            <td>{0}</td>", clienteStr))
                    sb.AppendLine(String.Format("            <td style=""font-size: 11px;"">{0}</td>", System.Net.WebUtility.HtmlEncode(p.Titulo)))
                    sb.AppendLine(String.Format("            <td><span style=""font-size: 10px; background: #e2e8f0; padding: 2px 4px; border-radius: 3px;"">{0}</span></td>", System.Net.WebUtility.HtmlEncode(p.EstatusNombre)))
                    sb.AppendLine(String.Format("            <td style=""text-align: center; font-size: 11px;"">{0:dd/MM/yy}</td>", p.FechaUltimoMovimiento))
                    sb.AppendLine(String.Format("            <td style=""text-align: center; font-weight: {0}; color: {1};"">{2}d</td>",
                                                If(p.DiasSinMovimiento >= 4, "bold", "normal"),
                                                If(p.DiasSinMovimiento > 15, "#b91c1c", If(p.DiasSinMovimiento >= 4, "#d97706", "#16a34a")),
                                                p.DiasSinMovimiento))
                    sb.AppendLine(String.Format("            <td style=""text-align: right; font-weight: bold;"">{0:C2} <span style=""font-size: 10px; color: #64748b;"">{1}</span></td>", p.TotalMonto, p.MonedaSiglas))
                    sb.AppendLine(String.Format("            <td style=""font-size: 10px; line-height: 1.3;"">{0}<br/><strong style=""color: #1e3a8a;"">{1}</strong></td>",
                                                System.Net.WebUtility.HtmlEncode(p.ProximaAccion), System.Net.WebUtility.HtmlEncode(p.Responsable)))
                    sb.AppendLine(String.Format("            <td style=""text-align: center; font-size: 10px;"">{0}</td>", fchCompStr))
                    sb.AppendLine(String.Format("            <td style=""text-align: center;""><span class=""{0}"" style=""{1}"">{2}</span></td>", badgeCls, badgeStyle, p.Semaforo))
                    sb.AppendLine("          </tr>")
                Next

                sb.AppendLine("        </tbody>")
                sb.AppendLine("      </table>")
            Next

            sb.AppendLine("    </div>")
        Next

        Return sb.ToString()
    End Function

    ''' <summary>
    ''' Sección 8: Apartado Exclusivo para Proyectos DECLINADOS (activo = 'CERRADO').
    ''' Presenta la ficha comercial del proyecto y el historial cronológico completo de seguimiento registrado en bitácora.
    ''' </summary>
    Private Function GenerarProyectosDeclinadosHtml(ByVal proyectos As List(Of ItemProyectoInforme)) As String
        Dim sb As New System.Text.StringBuilder()

        Dim declinados = proyectos.Where(Function(p) p.EsDeclinado).ToList()

        sb.AppendLine("    <div class=""sec-heading"" style=""border-left-color: #475569; color: #1e293b;"">&#128683; 8. Apartado Exclusivo: Proyectos Declinados (activo = 'CERRADO')</div>")
        sb.AppendLine("    <p style=""font-size: 12px; color: #64748b; margin: -6px 0 16px 0;"">Relación de proyectos marcados en sistema con estatus <strong>CERRADO / DECLINADO</strong> fuera del embudo comercial activo, detallando la justificación y seguimiento registrado en bitácora.</p>")

        If declinados.Count = 0 Then
            sb.AppendLine("    <div style=""padding: 14px 18px; background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 6px; font-size: 12px; color: #64748b; margin-bottom: 20px;"">")
            sb.AppendLine("      &#10003; <strong>Sin proyectos declinados:</strong> No se identificaron proyectos con estatus cerrado o declinado en el periodo evaluado.")
            sb.AppendLine("    </div>")
            Return sb.ToString()
        End If

        For Each p In declinados
            sb.AppendLine("    <div style=""margin-bottom: 24px; border: 1px solid #cbd5e1; border-radius: 6px; overflow: hidden; background-color: #ffffff; box-shadow: 0 1px 3px rgba(0,0,0,0.05);"">")

            ' Encabezado del proyecto declinado
            sb.AppendLine("      <div style=""background-color: #334155; color: #ffffff !important; padding: 10px 16px; font-size: 13px; font-weight: 700;"">")
            sb.AppendLine("        <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" style=""width: 100%; border-collapse: collapse;"">")
            sb.AppendLine("          <tr>")
            sb.AppendLine(String.Format("            <td style=""color: #ffffff !important;""><span style=""background-color: #dc2626; color: #ffffff !important; padding: 2px 8px; border-radius: 4px; font-size: 10px; font-weight: 800; letter-spacing: 0.5px; text-transform: uppercase; margin-right: 8px;"">DECLINADO</span> Folio: <span style=""font-family: Consolas, monospace; font-size: 14px;"">{0}</span> &bull; {1}</td>",
                                        p.ProyectoId, System.Net.WebUtility.HtmlEncode(p.ClienteNombre)))
            sb.AppendLine(String.Format("            <td style=""text-align: right; color: #ffffff !important; font-weight: 800; font-size: 13px;"">{0:C2} {1}</td>", p.TotalMonto, p.MonedaSiglas))
            sb.AppendLine("          </tr>")
            sb.AppendLine("        </table>")
            sb.AppendLine("      </div>")

            ' Ficha técnica y comercial
            sb.AppendLine("      <div style=""padding: 12px 16px; background-color: #f8fafc; border-bottom: 1px solid #e2e8f0; font-size: 12px; color: #334155;"">")
            sb.AppendLine("        <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" style=""width: 100%; border-collapse: collapse;"">")
            sb.AppendLine("          <tr>")
            sb.AppendLine(String.Format("            <td style=""width: 50%; vertical-align: top; padding-right: 10px;""><strong>Descripción:</strong> {0}<br/><strong>Clasificación:</strong> {1}</td>",
                                        System.Net.WebUtility.HtmlEncode(p.Titulo), System.Net.WebUtility.HtmlEncode(p.ClasificacionNombre)))
            sb.AppendLine(String.Format("            <td style=""width: 50%; vertical-align: top;""><strong>Vendedor:</strong> {0}<br/><strong>Fecha Registro:</strong> {1:dd/MM/yyyy} &bull; <strong>Último Movimiento:</strong> {2:dd/MM/yyyy} ({3} días)</td>",
                                        System.Net.WebUtility.HtmlEncode(p.VendedorNombre), p.FechaCreacion, p.FechaUltimoMovimiento, p.DiasSinMovimiento))
            sb.AppendLine("          </tr>")
            sb.AppendLine("        </table>")
            sb.AppendLine("      </div>")

            ' Sección de bitácora y seguimiento
            sb.AppendLine("      <div style=""padding: 14px 16px;"">")
            sb.AppendLine("        <div style=""font-size: 12px; font-weight: 700; color: #0f172a; margin-bottom: 8px;"">&#128220; Trazabilidad y Seguimiento Registrado en Bitácora:</div>")

            If p.Seguimientos IsNot Nothing AndAlso p.Seguimientos.Count > 0 Then
                sb.AppendLine("        <table class=""data-table"" style=""margin-bottom: 0;"">")
                sb.AppendLine("          <thead>")
                sb.AppendLine("            <tr>")
                sb.AppendLine("              <th style=""width: 5%; text-align: center;"">#</th>")
                sb.AppendLine("              <th style=""width: 16%; text-align: center;"">Fecha / Hora</th>")
                sb.AppendLine("              <th style=""width: 24%;"">Usuario Registrador</th>")
                sb.AppendLine("              <th style=""width: 55%;"">Detalle del Seguimiento Registrado</th>")
                sb.AppendLine("            </tr>")
                sb.AppendLine("          </thead>")
                sb.AppendLine("          <tbody>")

                Dim idx As Integer = 1
                For Each seg In p.Seguimientos
                    Dim esNotaResolucion As Boolean = seg.Detalle.IndexOf("declina", StringComparison.OrdinalIgnoreCase) >= 0 OrElse _
                                                     seg.Detalle.IndexOf("cancela", StringComparison.OrdinalIgnoreCase) >= 0 OrElse _
                                                     seg.Detalle.IndexOf("cerrad", StringComparison.OrdinalIgnoreCase) >= 0

                    Dim rowBg As String = If(esNotaResolucion, "background-color: #fef2f2;", "")
                    Dim textStyle As String = If(esNotaResolucion, "color: #991b1b; font-weight: 600;", "color: #334155;")
                    Dim badgeResolucion As String = If(esNotaResolucion, "<span style=""display: inline-block; background-color: #dc2626; color: #ffffff !important; font-size: 9px; font-weight: 700; padding: 1px 5px; border-radius: 3px; margin-right: 4px;"">RESOLUCIÓN</span> ", "")

                    sb.AppendLine(String.Format("            <tr style=""{0}"">", rowBg))
                    sb.AppendLine(String.Format("              <td style=""text-align: center; font-size: 11px; color: #64748b;"">{0}</td>", idx))
                    sb.AppendLine(String.Format("              <td style=""text-align: center; font-size: 11px;"">{0:dd/MM/yyyy HH:mm}</td>", seg.Fecha))
                    sb.AppendLine(String.Format("              <td style=""font-size: 11px;""><strong>{0}</strong><br/><span style=""font-size: 10px; color: #64748b;"">{1}</span></td>",
                                                System.Net.WebUtility.HtmlEncode(seg.UsuarioNombre), System.Net.WebUtility.HtmlEncode(seg.UsuarioClave)))
                    sb.AppendLine(String.Format("              <td style=""font-size: 11px; line-height: 1.4; {0}"">{1}{2}</td>",
                                                textStyle, badgeResolucion, System.Net.WebUtility.HtmlEncode(seg.Detalle).Replace(vbCrLf, "<br/>").Replace(vbLf, "<br/>")))
                    sb.AppendLine("            </tr>")
                    idx += 1
                Next

                sb.AppendLine("          </tbody>")
                sb.AppendLine("        </table>")
            Else
                sb.AppendLine("        <div style=""font-size: 11px; color: #64748b; font-style: italic; padding: 6px 0;"">No se identificaron notas registradas en bitácora de seguimiento para este proyecto.</div>")
            End If

            sb.AppendLine("      </div>")
            sb.AppendLine("    </div>")
        Next

        Return sb.ToString()
    End Function

#End Region

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
