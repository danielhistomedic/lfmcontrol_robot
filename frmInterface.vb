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
    Private _estaReconectandoCentral As Integer = 0
    Private WithEvents TimerReconexionCentral As New System.Timers.Timer()
    Private ReadOnly _lockReconexion As New Object()

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

    ' =========================================================================
    ' Control de Notificación Diaria de Seguimiento a Vendedores (Cotizaciones y Cotizaciones Internas)
    ' =========================================================================
    Private _procesandoSeguimientoVentas As Boolean = False
    Private _fechaUltimoEnvioSeguimientoVentas As Nullable(Of DateTime) = Nothing

    ' =========================================================================
    ' Control de Notificación Diaria de Seguimiento a Compras (Oportunidades en proceso de cotización a las 8:20 AM)
    ' =========================================================================
    Private _procesandoSeguimientoCompras As Boolean = False
    Private _fechaUltimoEnvioSeguimientoComprasFlowserve As Nullable(Of DateTime) = Nothing
    Private _fechaUltimoEnvioSeguimientoComprasDiversos As Nullable(Of DateTime) = Nothing

    ' =========================================================================
    ' Control de Sincronización de Pases de Salida (Servidor Central -> Local)
    ' =========================================================================
    Private _sincronizandoPasesSalida As Boolean = False
    Private ReadOnly _lockPasesSalida As New Object()

    ' =========================================================================
    ' Control de Sincronización de Archivos Adjuntos Pases de Salida
    ' =========================================================================
    Private _procesandoAdjuntosPasesSalida As Boolean = False
    Private ReadOnly _lockAdjuntosPasesSalida As New Object()


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
    ''' <summary>
    ''' Notifica y cambia el estado a desconectado, e inicia la rutina de reconexión controlada.
    ''' Es segura para ser llamada desde cualquier hilo (UI o segundo plano).
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
    ''' Inicia la rutina de reconexión controlada para el servidor central con temporizador multihilo seguro.
    ''' Se ejecuta solo mientras la conexión central esté inactiva/desconectada.
    ''' </summary>
    Private Sub IniciarRutinaReconexionCentral(Optional ByVal inmediato As Boolean = False)
        If _servidorCentralConectado Then
            DetenerRutinaReconexionCentral()
            Exit Sub
        End If

        SyncLock _lockReconexion
            If _estaReconectandoCentral <> 0 Then
                Exit Sub
            End If

            If inmediato Then
                TimerReconexionCentral.Stop()
                _intentosReconexionCentral = 0
                Task.Run(Sub() EjecutarReconexionCentralAsync())
                Exit Sub
            End If

            If Not TimerReconexionCentral.Enabled Then
                ' Primer intento tras una breve pausa de 20 segundos (permite estabilizar parpadeos de red sin saturar HostGator)
                TimerReconexionCentral.AutoReset = False
                TimerReconexionCentral.Interval = 20000 ' 20 segundos
                TimerReconexionCentral.Start()
                AgregarLog(100, "[Reconexión Central] Rutina de reconexión activada. Primer reintento seguro en 20 segundos (Protección HostGator)...")
            End If
        End SyncLock
    End Sub

    ''' <summary>
    ''' Detiene y desactiva la rutina de reconexión una vez restablecida la conexión central.
    ''' </summary>
    Private Sub DetenerRutinaReconexionCentral()
        SyncLock _lockReconexion
            TimerReconexionCentral.Stop()
            _intentosReconexionCentral = 0
            Threading.Interlocked.Exchange(_estaReconectandoCentral, 0)
        End SyncLock
    End Sub

    ''' <summary>
    ''' Cierra de manera segura cualquier conexión central existente para liberar sockets y recursos antes de reconectar.
    ''' Limpia los pools de conexión de MySqlConnector para evitar reutilizar sockets rotos.
    ''' </summary>
    Private Sub CerrarConexionesCentrales()
        Try
            If cx_MySQL_Central IsNot Nothing Then
                If cx_MySQL_Central.State <> ConnectionState.Closed Then cx_MySQL_Central.Close()
                MySqlConnector.MySqlConnection.ClearPool(cx_MySQL_Central)
            End If
        Catch ex As Exception
        End Try

        Try
            If cx_MySQL_CentralAsync IsNot Nothing Then
                If cx_MySQL_CentralAsync.State <> ConnectionState.Closed Then cx_MySQL_CentralAsync.Close()
                MySqlConnector.MySqlConnection.ClearPool(cx_MySQL_CentralAsync)
            End If
        Catch ex As Exception
        End Try

        Try
            If cx_MySQL_CentralAsyncALM IsNot Nothing Then
                If cx_MySQL_CentralAsyncALM.State <> ConnectionState.Closed Then cx_MySQL_CentralAsyncALM.Close()
                MySqlConnector.MySqlConnection.ClearPool(cx_MySQL_CentralAsyncALM)
            End If
        Catch ex As Exception
        End Try

        Try
            If cx_MySQL_Admin IsNot Nothing Then
                If cx_MySQL_Admin.State <> ConnectionState.Closed Then cx_MySQL_Admin.Close()
                MySqlConnector.MySqlConnection.ClearPool(cx_MySQL_Admin)
            End If
        Catch ex As Exception
        End Try
    End Sub

    ''' <summary>
    ''' Manejador del temporizador multihilo System.Timers.Timer para la reconexión al servidor central.
    ''' Se ejecuta en segundo plano sin congelar la UI de la aplicación.
    ''' </summary>
    Private Sub TimerReconexionCentral_Elapsed(sender As Object, e As System.Timers.ElapsedEventArgs) Handles TimerReconexionCentral.Elapsed
        EjecutarReconexionCentralAsync()
    End Sub

    ''' <summary>
    ''' Ejecuta el proceso de reconexión segura con el servidor central:
    ''' - Se ejecuta en segundo plano garantizando que la UI nunca se congele.
    ''' - Usa Interlocked para evitar colisiones entre reconexiones simultáneas.
    ''' - Aplica una política de backoff progresivo (20s, 60s, 180s, 300s) para no ser bloqueado por cPHulk/CSF de HostGator.
    ''' </summary>
    Private Sub EjecutarReconexionCentralAsync()
        If Threading.Interlocked.CompareExchange(_estaReconectandoCentral, 1, 0) <> 0 Then
            Exit Sub
        End If

        Try
            If _servidorCentralConectado Then
                DetenerRutinaReconexionCentral()
                Exit Sub
            End If

            _intentosReconexionCentral += 1
            AgregarLog(100, "[Reconexión Central] Ejecutando intento seguro de reconexión #" & _intentosReconexionCentral & "...")

            ' Cerrar sockets/conexiones previas y limpiar pools antes de reintentar
            CerrarConexionesCentrales()

            ' Si CLUES está vacía, intentar recuperarla desde la base de datos local
            If String.IsNullOrWhiteSpace(Me.CLUES) Then
                Try
                    Dim tb_cat As DataTable = tb_Recordset_MySQL_local("Select cClues, cSiglas from cat_consultorio")
                    If tb_cat IsNot Nothing AndAlso tb_cat.Rows.Count > 0 Then
                        Me.CLUES = tb_cat.Rows(0).Item("cClues").ToString
                        Me.cSiglas = tb_cat.Rows(0).Item("cSiglas").ToString
                    End If
                Catch exClues As Exception
                End Try
            End If

            Dim reconectado As Boolean = False
            If Not String.IsNullOrWhiteSpace(Me.CLUES) Then
                reconectado = Me.Conectar_Central(Me.CLUES)
            Else
                AgregarLog(500, "[Reconexión Central] No se pudo obtener la clave CLUES para reconectar.")
            End If

            If reconectado Then
                _servidorCentralConectado = True
                _intentosReconexionCentral = 0
                Me.HabilitarEstatusConexionCentral(True)

                AgregarLog(200, "[Reconexión Central] ¡Conexión con el servidor central restablecida con éxito!")

                ' Reactivar procesos de sincronización
                If Me.chkActivar.Checked Then
                    Me.ReiniciarProcesoSP()
                Else
                    If Me.InvokeRequired Then
                        Me.Invoke(Sub() Me.chkActivar.Checked = True)
                    Else
                        Me.chkActivar.Checked = True
                    End If
                End If

                DetenerRutinaReconexionCentral()
            Else
                ' Falló el intento: Programar siguiente según la política anti-bloqueo de HostGator
                Dim proximoIntervaloMs As Integer = 300000
                Dim textoIntervalo As String = "5 minutos (Protección Anti-Bloqueo HostGator activa)"

                Select Case _intentosReconexionCentral
                    Case 1
                        proximoIntervaloMs = 60000 ' 1 minuto
                        textoIntervalo = "1 minuto"
                    Case 2
                        proximoIntervaloMs = 180000 ' 3 minutos
                        textoIntervalo = "3 minutos"
                    Case Else
                        ' A partir del 3er intento fallido: 5 minutos
                        ' HostGator bloquea IPs por conexiones fallidas repetidas (cPHulk / CSF).
                        proximoIntervaloMs = 300000 ' 5 minutos
                        textoIntervalo = "5 minutos (Protección Anti-Bloqueo HostGator activa)"
                End Select

                AgregarLog(500, "[Reconexión Central] Intento #" & _intentosReconexionCentral & " no completado. Próximo intento programado en " & textoIntervalo & ".")

                TimerReconexionCentral.AutoReset = False
                TimerReconexionCentral.Interval = proximoIntervaloMs
                TimerReconexionCentral.Start()
            End If

        Catch ex As Exception
            AgregarLog(500, "[Reconexión Central] Error en proceso de reconexión: " & ex.Message & ". Reintentando en 5 minutos.")
            TimerReconexionCentral.AutoReset = False
            TimerReconexionCentral.Interval = 300000
            TimerReconexionCentral.Start()
        Finally
            Threading.Interlocked.Exchange(_estaReconectandoCentral, 0)
        End Try
    End Sub

    Private Function Conectar_Central(clues) As Boolean

        Try

            Dim Database As String = ""
            Dim Uid As String = ""
            Dim Pwd As String = ""

            ' ==================================================================================================================================
            ' Conexión al servidor de administración y licencias (histomedic.mx)
            ' Parámetros de timeout y keepalive para evitar cuelgues de socket
            Dim cadena_conexion_admin As String = "Server=histomedic.mx;Database=mirtheda_admin;Uid=mirtheda_root;Pwd=Bsapmd2cKb*5;SSL Mode=None;Connection Timeout=10;Default Command Timeout=30;Keepalive=60;"

            If cx_MySQL_Admin Is Nothing OrElse cx_MySQL_Admin.State <> ConnectionState.Open Then
                If Not Test_MySQL_Admin(cadena_conexion_admin) Then
                    _servidorCentralConectado = False
                    Me.HabilitarEstatusConexionCentral(False)
                    Return False
                End If
            End If

            tb_ClienteData = tb_Recordset_MySQL_Admin("SELECT * FROM ssf_clientes WHERE clues = '" & clues & "'")
            If tb_ClienteData Is Nothing OrElse tb_ClienteData.Rows.Count = 0 Then
                AgregarLog(500, "[Central] Error de conexión con el Servidor (Cliente no encontrado en ssf_clientes).")
                _servidorCentralConectado = False
                Me.HabilitarEstatusConexionCentral(False)
                Return False
            End If

            Database = tb_ClienteData.Rows(0).Item("db_name").ToString
            Uid = tb_ClienteData.Rows(0).Item("db_user").ToString
            Pwd = tb_ClienteData.Rows(0).Item("db_pass").ToString

            If tb_ClienteData.Rows(0).Item("actualizaciones").ToString <> "SI" Then
                AgregarLog(500, "[Central] No disponible para actualizaciones.")
                _servidorCentralConectado = False
                Me.HabilitarEstatusConexionCentral(False)
                Return False
            End If

            ' ==================================================================================================================================
            ' Conexión a la base de datos de hosting del cliente (lfmcontrol.com.mx en HostGator)
            Dim cadena_conexion As String = "Server=lfmcontrol.com.mx;Database=" & Database & ";Uid=" & Uid & ";Pwd=" & Pwd & ";SSL Mode=None;Connection Timeout=10;Default Command Timeout=30;Keepalive=60;"

            CerrarConexionesCentrales()

            ' Validar que los 3 canales de conexión con HostGator se abran correctamente
            If Test_MySQL_Central(cadena_conexion) AndAlso
               Test_MySQL_CentralAsync(cadena_conexion) AndAlso
               Test_MySQL_CentralAsyncALM(cadena_conexion) Then

                _servidorCentralConectado = True
                Me.HabilitarEstatusConexionCentral(True)
                DetenerRutinaReconexionCentral()
                Return True
            Else
                CerrarConexionesCentrales()
                _servidorCentralConectado = False
                Me.HabilitarEstatusConexionCentral(False)
                Return False
            End If

        Catch ex As Exception
            CerrarConexionesCentrales()
            _servidorCentralConectado = False
            Me.HabilitarEstatusConexionCentral(False)
            AgregarLog(500, "[Central] Error al conectar a Servidor Central: " & ex.Message)
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
                    Me.ExportarAdjuntosPasesSalida()
                Catch ex As Exception
                    LogEventos.Escribir("ExportarAdjuntosPasesSalida. " & ex.Message)
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

                ' Notificación Diaria de Seguimiento a Vendedores (Cotizaciones y Cotizaciones Internas a las 8:15 AM de lunes a viernes)
                If Not _procesandoSeguimientoVentas Then
                    Me.NotificarSeguimientoCotizacionesVentas()
                End If

                ' Notificación Diaria de Seguimiento a Compras (Oportunidades en proceso de cotización a las 8:20 AM de lunes a viernes)
                If Not _procesandoSeguimientoCompras Then
                    Me.NotificarSeguimientoOportunidadesCompras()
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
                Try
                    Me.ExportarDataToHostingSP()
                Catch exdatauno As Exception
                    LogEventos.Escribir(String.Format("[ExportarDataToHostingSP] Error no controlado en ciclo de descarga: {0}", exdatauno.Message))
                End Try
                Try
                    Me.ExportarDataToHostingSP_Almacen()
                Catch exdatados As Exception
                    LogEventos.Escribir(String.Format("[ExportarDataToHostingSP_Almacen] Error no controlado en ciclo de descarga: {0}", exdatados.Message))
                End Try

                Try
                    Me.DescargarPasesSalidaCentral()
                Catch exPases As Exception
                    LogEventos.Escribir(String.Format("[Pases Salida] Error no controlado en ciclo de descarga: {0}", exPases.Message))
                End Try
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
                            },
                              {
                                "tb_pases_salida",
                                New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
                                    "sinc",
                                    "firma_recibe",
                                    "fch_usuario_recibe",
                                    "nombre_recibio_salida",
                                    "ccveusuario_recibe",
                                    "enviado"
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

    Public Sub AgregarLog(ByVal tiempo As Integer, ByVal error_s As String)
        Try
            If lstLog.InvokeRequired Then
                lstLog.Invoke(Sub()
                                  Dim item = lstLog.Items.Add(Calcula_FechaActual().ToString())
                                  item.SubItems.Add(error_s)
                                  item.EnsureVisible()
                              End Sub)
            Else
                Dim item = Me.lstLog.Items.Add(Calcula_FechaActual().ToString())
                item.SubItems.Add(error_s)
                item.EnsureVisible()
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
        Try
            If cx_MySQL_Admin IsNot Nothing AndAlso cx_MySQL_Admin.State = ConnectionState.Open Then
                Return True
            End If

            If cx_MySQL_Admin Is Nothing Then
                cx_MySQL_Admin = New MySqlConnector.MySqlConnection()
            ElseIf cx_MySQL_Admin.State <> ConnectionState.Closed Then
                Try
                    cx_MySQL_Admin.Close()
                Catch
                End Try
            End If

            cx_MySQL_Admin.ConnectionString = str_ConStr
            cx_MySQL_Admin.Open()

            Try
                Using cmd As New MySqlConnector.MySqlCommand("SET time_zone = '-06:00';", cx_MySQL_Admin)
                    cmd.CommandTimeout = 10
                    cmd.ExecuteNonQuery()
                End Using
            Catch exZone As Exception
            End Try

            Return True
        Catch ex As Exception
            LogEventos.Escribir("Error Test_MySQL_Admin: " & ex.Message)
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
        Try
            If cx_MySQL_CentralAsync IsNot Nothing AndAlso cx_MySQL_CentralAsync.State = ConnectionState.Open Then
                Return True
            End If

            If cx_MySQL_CentralAsync Is Nothing Then
                cx_MySQL_CentralAsync = New MySqlConnector.MySqlConnection()
            ElseIf cx_MySQL_CentralAsync.State <> ConnectionState.Closed Then
                Try
                    cx_MySQL_CentralAsync.Close()
                Catch
                End Try
            End If

            cx_MySQL_CentralAsync.ConnectionString = str_ConStr
            cx_MySQL_CentralAsync.Open()

            Try
                Using cmd As New MySqlConnector.MySqlCommand("SET time_zone = '-06:00';", cx_MySQL_CentralAsync)
                    cmd.CommandTimeout = 10
                    cmd.ExecuteNonQuery()
                End Using
            Catch exZone As Exception
            End Try

            Return True
        Catch ex As Exception
            LogEventos.Escribir("Error Test_MySQL_CentralAsync: " & ex.Message)
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
        Try
            If cx_MySQL_CentralAsyncALM IsNot Nothing AndAlso cx_MySQL_CentralAsyncALM.State = ConnectionState.Open Then
                Return True
            End If

            If cx_MySQL_CentralAsyncALM Is Nothing Then
                cx_MySQL_CentralAsyncALM = New MySqlConnector.MySqlConnection()
            ElseIf cx_MySQL_CentralAsyncALM.State <> ConnectionState.Closed Then
                Try
                    cx_MySQL_CentralAsyncALM.Close()
                Catch
                End Try
            End If

            cx_MySQL_CentralAsyncALM.ConnectionString = str_ConStr
            cx_MySQL_CentralAsyncALM.Open()

            Try
                Using cmd As New MySqlConnector.MySqlCommand("SET time_zone = '-06:00';", cx_MySQL_CentralAsyncALM)
                    cmd.CommandTimeout = 10
                    cmd.ExecuteNonQuery()
                End Using
            Catch exZone As Exception
            End Try

            Return True
        Catch ex As Exception
            LogEventos.Escribir("Error Test_MySQL_CentralAsyncALM: " & ex.Message)
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
        Try
            If cx_MySQL_Central IsNot Nothing AndAlso cx_MySQL_Central.State = ConnectionState.Open Then
                Return True
            End If

            If cx_MySQL_Central Is Nothing Then
                cx_MySQL_Central = New MySqlConnector.MySqlConnection()
            ElseIf cx_MySQL_Central.State <> ConnectionState.Closed Then
                Try
                    cx_MySQL_Central.Close()
                Catch
                End Try
            End If

            cx_MySQL_Central.ConnectionString = str_ConStr
            cx_MySQL_Central.Open()

            Try
                Using cmd As New MySqlConnector.MySqlCommand("SET time_zone = '-06:00';", cx_MySQL_Central)
                    cmd.CommandTimeout = 10
                    cmd.ExecuteNonQuery()
                End Using
            Catch exZone As Exception
            End Try

            Return True
        Catch ex As Exception
            LogEventos.Escribir("Error Test_MySQL_Central: " & ex.Message)
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

        TimerReconexionCentral.AutoReset = False

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
            AgregarLog(100, "[Manual] Verificando e intentando reconexión inmediata al servidor central...")
            IniciarRutinaReconexionCentral(inmediato:=True)
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

    ''' <summary>
    ''' Actualiza sinc = 0 para el registro especificado mediante consulta SQL parametrizada por su llave primaria.
    ''' </summary>
    Private Function ActualizarSincronizadoLocal(nombreTabla As String, nombreCampoPk As String, valorId As Object) As Boolean
        Try
            If cx_MySQL_local.State = ConnectionState.Closed Then
                cx_MySQL_local.Open()
            End If

            Dim sql As String = String.Format("UPDATE {0} SET sinc = 0 WHERE {1} = @id;", nombreTabla, nombreCampoPk)
            Using cmd As New MySqlConnector.MySqlCommand(sql, cx_MySQL_local)
                cmd.Parameters.Add("@id", MySqlConnector.MySqlDbType.Int32).Value = Convert.ToInt32(valorId)
                Dim rowsAffected As Integer = cmd.ExecuteNonQuery()
                Return rowsAffected > 0
            End Using
        Catch ex As Exception
            LogEventos.Escribir(String.Format("ActualizarSincronizadoLocal - Tabla: {0} - {1}: {2} - Error: {3}", nombreTabla, nombreCampoPk, valorId, ex.Message))
            Return False
        End Try
    End Function

    ''' <summary>
    ''' Registra en Eventos.log un error o rechazo en la sincronización de un archivo adjunto.
    ''' </summary>
    Private Sub RegistrarErrorAdjunto(funcion As String, idRegistro As Object, nombreArchivo As String, tipoError As String, detalleError As String)
        Dim strId As String = If(idRegistro IsNot Nothing AndAlso Not IsDBNull(idRegistro), idRegistro.ToString(), "N/A")
        Dim strArchivo As String = If(Not String.IsNullOrWhiteSpace(nombreArchivo), nombreArchivo, "Desconocido")
        Dim mensaje As String = String.Format("{0} - ID: {1} - Archivo: {2} - Error [{3}]: {4}", funcion, strId, strArchivo, tipoError, detalleError)
        LogEventos.Escribir(mensaje)
    End Sub

    Public Sub ExportarAdjuntos()

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

            Dim tempFolder As String = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HistoMedic_Temp")
            If Not System.IO.Directory.Exists(tempFolder) Then
                System.IO.Directory.CreateDirectory(tempFolder)
            End If

            For Each tabla As String In tablas
                Try
                    Dim query As String = "SELECT * FROM " & tabla & " WHERE sinc = 1"
                    Dim dt As DataTable = tb_Recordset_MySQL_local(query)

                    If dt IsNot Nothing AndAlso dt.Rows.Count > 0 Then
                        For Each row As DataRow In dt.Rows
                            Dim idRegistro As Object = If(dt.Columns.Contains("id") AndAlso Not IsDBNull(row("id")), row("id"), "N/A")
                            Dim nombreArchivo As String = ""

                            Try
                                ' Validar campo archivo: si es NULL, Nothing, cadena vacía o espacios, ignorar y continuar sin registrar error
                                If String.IsNullOrWhiteSpace(Convert.ToString(row("archivo"))) Then
                                    Continue For
                                End If

                                nombreArchivo = Convert.ToString(row("archivo")).Trim()
                                Dim nombreArchivoLimpio As String = System.IO.Path.GetFileName(nombreArchivo)
                                If String.IsNullOrWhiteSpace(nombreArchivoLimpio) Then
                                    Continue For
                                End If

                                Dim rutaTemporalLocal As String = System.IO.Path.Combine(tempFolder, nombreArchivoLimpio)

                                ' 1. Descarga exclusiva desde FTP local /TB_VENTAS/
                                Dim localizado As Boolean = False
                                Dim rutaRemotaLocal As String = localFtpHost & "/TB_VENTAS/" & nombreArchivoLimpio
                                Try
                                    Dim descargado As Boolean = ftpLocalClient.DescargarArchivo(rutaRemotaLocal, rutaTemporalLocal)
                                    If descargado AndAlso System.IO.File.Exists(rutaTemporalLocal) Then
                                        localizado = True
                                    End If
                                Catch exDescarga As Exception
                                    RegistrarErrorAdjunto("ExportarAdjuntos", idRegistro, nombreArchivoLimpio, "DescargaFTP", "Excepción al descargar de FTP local (" & rutaRemotaLocal & "): " & exDescarga.Message)
                                End Try

                                ' 2. Validar que el archivo exista físicamente y sea legible
                                If Not localizado OrElse Not System.IO.File.Exists(rutaTemporalLocal) Then
                                    RegistrarErrorAdjunto("ExportarAdjuntos", idRegistro, nombreArchivoLimpio, "NoEncontrado", "El archivo no existe o no se pudo localizar en TB_VENTAS. Se mantiene sinc = 1.")
                                    Continue For
                                End If

                                Dim fi As New System.IO.FileInfo(rutaTemporalLocal)
                                If fi.Length = 0 Then
                                    RegistrarErrorAdjunto("ExportarAdjuntos", idRegistro, nombreArchivoLimpio, "ArchivoVacio", "El archivo tiene tamaño 0 bytes. Se mantiene sinc = 1.")
                                    Continue For
                                End If

                                ' 3. Subir al hosting mediante SubirArchivoHosting (FTPHosting)
                                Dim subido As Boolean = False
                                Try
                                    subido = SubirArchivoHosting(rutaTemporalLocal, nombreArchivoLimpio, "/sistema.lfmcontrol.com.mx/Assets/files/ventas/")
                                Catch exSubida As Exception
                                    RegistrarErrorAdjunto("ExportarAdjuntos", idRegistro, nombreArchivoLimpio, "FTPHosting", "Excepción en SubirArchivoHosting: " & exSubida.Message)
                                    subido = False
                                End Try

                                ' 4. Únicamente cuando SubirArchivo = True se actualiza sinc = 0
                                If subido Then
                                    Dim actualizado As Boolean = ActualizarSincronizadoLocal(tabla, "id", idRegistro)
                                    If actualizado Then
                                        LogEventos.Escribir(String.Format("ExportarAdjuntos - Tabla: {0} - ID: {1} - Archivo: {2} - Cargado exitosamente y actualizado sinc = 0.", tabla, idRegistro, nombreArchivoLimpio))
                                    Else
                                        RegistrarErrorAdjunto("ExportarAdjuntos", idRegistro, nombreArchivoLimpio, "BaseDatos", "Archivo cargado a hosting pero falló el UPDATE sinc = 0 en tabla " & tabla)
                                    End If
                                Else
                                    RegistrarErrorAdjunto("ExportarAdjuntos", idRegistro, nombreArchivoLimpio, "FTPHosting", "FTPHosting.SubirArchivo devolvió False. Se mantiene sinc = 1.")
                                End If

                            Catch exRow As Exception
                                RegistrarErrorAdjunto("ExportarAdjuntos", idRegistro, nombreArchivo, "Excepcion", "Error no controlado procesando registro: " & exRow.Message)
                            Finally
                                ' Limpieza de archivo temporal si quedó remanente
                                Try
                                    If Not String.IsNullOrWhiteSpace(nombreArchivo) Then
                                        Dim rutaTempClean As String = System.IO.Path.Combine(tempFolder, System.IO.Path.GetFileName(nombreArchivo))
                                        If System.IO.File.Exists(rutaTempClean) Then
                                            System.IO.File.Delete(rutaTempClean)
                                        End If
                                    End If
                                Catch exClean As Exception
                                End Try
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

    Public Sub ExportarAdjuntosFotosMaterial()

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

            Dim query As String = "SELECT * FROM tb_materiales_ftp WHERE sinc = 1"
            Dim dt As DataTable = tb_Recordset_MySQL_local(query)

            If dt Is Nothing OrElse dt.Rows.Count = 0 Then Exit Sub

            Dim tempFolder As String = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HistoMedic_Temp")
            If Not System.IO.Directory.Exists(tempFolder) Then
                System.IO.Directory.CreateDirectory(tempFolder)
            End If

            For Each row As DataRow In dt.Rows
                Dim idRegistro As Object = If(dt.Columns.Contains("Id") AndAlso Not IsDBNull(row("Id")), row("Id"), "N/A")
                If idRegistro.ToString() = "N/A" AndAlso dt.Columns.Contains("id") AndAlso Not IsDBNull(row("id")) Then
                    idRegistro = row("id")
                End If

                Try
                    Dim totalImagenesDefinidas As Integer = 0
                    Dim imagenesSubidasOk As Integer = 0
                    Dim huboFalloEnAlgunaImagen As Boolean = False

                    For i As Integer = 1 To 5
                        ' Si la columna no tiene imagen válida, ignorar y continuar con la siguiente
                        If String.IsNullOrWhiteSpace(Convert.ToString(row("img" & i))) Then
                            Continue For
                        End If

                        Dim nombreArchivo As String = Convert.ToString(row("img" & i)).Trim()
                        Dim nombreArchivoLimpio As String = System.IO.Path.GetFileName(nombreArchivo)
                        If String.IsNullOrWhiteSpace(nombreArchivoLimpio) Then
                            Continue For
                        End If

                        totalImagenesDefinidas += 1
                        Dim rutaTemporalLocal As String = System.IO.Path.Combine(tempFolder, nombreArchivoLimpio)

                        Try
                            ' 1. Descarga exclusiva desde FTP local /TB_MATERIALES/
                            Dim localizado As Boolean = False
                            Dim rutaRemotaLocal As String = localFtpHost & "/TB_MATERIALES/" & nombreArchivoLimpio
                            Try
                                Dim descargado As Boolean = ftpLocalClient.DescargarArchivo(rutaRemotaLocal, rutaTemporalLocal)
                                If descargado AndAlso System.IO.File.Exists(rutaTemporalLocal) Then
                                    localizado = True
                                End If
                            Catch exDescarga As Exception
                                RegistrarErrorAdjunto("ExportarAdjuntosFotosMaterial", idRegistro, nombreArchivoLimpio, "DescargaFTP", "Excepción al descargar de FTP local (" & rutaRemotaLocal & "): " & exDescarga.Message)
                            End Try

                            ' Validar existencia y legibilidad
                            If Not localizado OrElse Not System.IO.File.Exists(rutaTemporalLocal) Then
                                huboFalloEnAlgunaImagen = True
                                RegistrarErrorAdjunto("ExportarAdjuntosFotosMaterial", idRegistro, nombreArchivoLimpio, "NoEncontrado", "Imagen img" & i & " no existe o no se pudo localizar en TB_MATERIALES. Se mantiene sinc = 1.")
                                Continue For
                            End If

                            Dim fi As New System.IO.FileInfo(rutaTemporalLocal)
                            If fi.Length = 0 Then
                                huboFalloEnAlgunaImagen = True
                                RegistrarErrorAdjunto("ExportarAdjuntosFotosMaterial", idRegistro, nombreArchivoLimpio, "ArchivoVacio", "Imagen img" & i & " tiene tamaño 0 bytes. Se mantiene sinc = 1.")
                                Continue For
                            End If

                            ' Subir al hosting de productos
                            Dim subido As Boolean = False
                            Try
                                subido = SubirArchivoHosting(rutaTemporalLocal, nombreArchivoLimpio, "/sistema.lfmcontrol.com.mx/Assets/files/productos/")
                            Catch exSubida As Exception
                                RegistrarErrorAdjunto("ExportarAdjuntosFotosMaterial", idRegistro, nombreArchivoLimpio, "FTPHosting", "Excepción al subir imagen img" & i & ": " & exSubida.Message)
                                subido = False
                            End Try

                            If subido Then
                                imagenesSubidasOk += 1
                            Else
                                huboFalloEnAlgunaImagen = True
                                RegistrarErrorAdjunto("ExportarAdjuntosFotosMaterial", idRegistro, nombreArchivoLimpio, "FTPHosting", "FTPHosting.SubirArchivo devolvió False para imagen img" & i & ". Se mantiene sinc = 1.")
                            End If

                        Catch exImg As Exception
                            huboFalloEnAlgunaImagen = True
                            RegistrarErrorAdjunto("ExportarAdjuntosFotosMaterial", idRegistro, nombreArchivoLimpio, "ExcepcionImagen", "Error al procesar imagen img" & i & ": " & exImg.Message)
                        Finally
                            Try
                                If System.IO.File.Exists(rutaTemporalLocal) Then
                                    System.IO.File.Delete(rutaTemporalLocal)
                                End If
                            Catch exClean As Exception
                            End Try
                        End Try
                    Next

                    ' Regla: Si hubo fallo en alguna imagen, NO actualizar sinc = 0. Mantener sinc = 1.
                    ' Solo actualizar sinc = 0 si todas las imágenes definidas fueron confirmadas exitosamente.
                    If Not huboFalloEnAlgunaImagen Then
                        If totalImagenesDefinidas > 0 AndAlso imagenesSubidasOk = totalImagenesDefinidas Then
                            Dim actualizado As Boolean = ActualizarSincronizadoLocal("tb_materiales_ftp", "Id", idRegistro)
                            If actualizado Then
                                LogEventos.Escribir(String.Format("ExportarAdjuntosFotosMaterial - ID: {0} - {1} imágenes cargadas exitosamente y actualizado sinc = 0.", idRegistro, imagenesSubidasOk))
                            Else
                                RegistrarErrorAdjunto("ExportarAdjuntosFotosMaterial", idRegistro, "", "BaseDatos", "Imágenes cargadas a hosting pero falló el UPDATE sinc = 0 en tb_materiales_ftp")
                            End If
                        ElseIf totalImagenesDefinidas = 0 Then
                            ' Sin imágenes adjuntas registradas en este registro
                            ActualizarSincronizadoLocal("tb_materiales_ftp", "Id", idRegistro)
                            LogEventos.Escribir(String.Format("ExportarAdjuntosFotosMaterial - ID: {0} - Sin imágenes adjuntas registradas. Actualizado sinc = 0.", idRegistro))
                        End If
                    Else
                        LogEventos.Escribir(String.Format("ExportarAdjuntosFotosMaterial - ID: {0} - Carga incompleta ({1}/{2} imágenes exitosas). Se mantiene sinc = 1.", idRegistro, imagenesSubidasOk, totalImagenesDefinidas))
                    End If

                Catch exRow As Exception
                    RegistrarErrorAdjunto("ExportarAdjuntosFotosMaterial", idRegistro, "", "Excepcion", "Error no controlado procesando registro: " & exRow.Message)
                End Try
            Next

        Catch exGeneral As Exception
            LogEventos.Escribir("Error general en ExportarAdjuntosFotosMaterial: " & exGeneral.Message)
        End Try

    End Sub

    Public Sub ExportarAdjuntosAlmacen()

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

            Dim tempFolder As String = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HistoMedic_Temp")
            If Not System.IO.Directory.Exists(tempFolder) Then
                System.IO.Directory.CreateDirectory(tempFolder)
            End If

            For Each tabla As String In tablas
                Try
                    Dim query As String = "SELECT * FROM " & tabla & " WHERE sinc = 1"
                    Dim dt As DataTable = tb_Recordset_MySQL_local(query)

                    If dt IsNot Nothing AndAlso dt.Rows.Count > 0 Then
                        Dim campoPk As String = "icvereciboscompdigitales"
                        If Not dt.Columns.Contains(campoPk) AndAlso dt.Columns.Contains("id") Then
                            campoPk = "id"
                        End If

                        For Each row As DataRow In dt.Rows
                            Dim idRegistro As Object = If(dt.Columns.Contains(campoPk) AndAlso Not IsDBNull(row(campoPk)), row(campoPk), "N/A")
                            Dim nombreArchivo As String = ""

                            Try
                                ' Validar campo documento_ftp: si es NULL, Nothing, cadena vacía o espacios, ignorar y continuar sin registrar error
                                If String.IsNullOrWhiteSpace(Convert.ToString(row("documento_ftp"))) Then
                                    Continue For
                                End If

                                nombreArchivo = Convert.ToString(row("documento_ftp")).Trim()
                                Dim nombreArchivoLimpio As String = System.IO.Path.GetFileName(nombreArchivo)
                                If String.IsNullOrWhiteSpace(nombreArchivoLimpio) Then
                                    Continue For
                                End If

                                Dim rutaTemporalLocal As String = System.IO.Path.Combine(tempFolder, nombreArchivoLimpio)

                                ' 1. Descarga exclusiva desde FTP local /TB_RECIBOS/
                                Dim localizado As Boolean = False
                                Dim rutaRemotaLocal As String = localFtpHost & "/TB_RECIBOS/" & nombreArchivoLimpio
                                Try
                                    Dim descargado As Boolean = ftpLocalClient.DescargarArchivo(rutaRemotaLocal, rutaTemporalLocal)
                                    If descargado AndAlso System.IO.File.Exists(rutaTemporalLocal) Then
                                        localizado = True
                                    End If
                                Catch exDescarga As Exception
                                    RegistrarErrorAdjunto("ExportarAdjuntosAlmacen", idRegistro, nombreArchivoLimpio, "DescargaFTP", "Excepción al descargar de FTP local (" & rutaRemotaLocal & "): " & exDescarga.Message)
                                End Try

                                ' 2. Validar que el archivo exista físicamente y sea legible
                                If Not localizado OrElse Not System.IO.File.Exists(rutaTemporalLocal) Then
                                    RegistrarErrorAdjunto("ExportarAdjuntosAlmacen", idRegistro, nombreArchivoLimpio, "NoEncontrado", "El archivo no existe o no se pudo localizar en TB_RECIBOS. Se mantiene sinc = 1.")
                                    Continue For
                                End If

                                Dim fi As New System.IO.FileInfo(rutaTemporalLocal)
                                If fi.Length = 0 Then
                                    RegistrarErrorAdjunto("ExportarAdjuntosAlmacen", idRegistro, nombreArchivoLimpio, "ArchivoVacio", "El archivo tiene tamaño 0 bytes. Se mantiene sinc = 1.")
                                    Continue For
                                End If

                                ' 3. Subir al hosting mediante SubirArchivoHosting (FTPHosting)
                                Dim subido As Boolean = False
                                Try
                                    subido = SubirArchivoHosting(rutaTemporalLocal, nombreArchivoLimpio, "/sistema.lfmcontrol.com.mx/Assets/files/ventas/")
                                Catch exSubida As Exception
                                    RegistrarErrorAdjunto("ExportarAdjuntosAlmacen", idRegistro, nombreArchivoLimpio, "FTPHosting", "Excepción en SubirArchivoHosting: " & exSubida.Message)
                                    subido = False
                                End Try

                                ' 4. Únicamente cuando SubirArchivo = True se actualiza sinc = 0
                                If subido Then
                                    Dim actualizado As Boolean = ActualizarSincronizadoLocal(tabla, campoPk, idRegistro)
                                    If actualizado Then
                                        LogEventos.Escribir(String.Format("ExportarAdjuntosAlmacen - Tabla: {0} - ID: {1} - Archivo: {2} - Cargado exitosamente y actualizado sinc = 0.", tabla, idRegistro, nombreArchivoLimpio))
                                    Else
                                        RegistrarErrorAdjunto("ExportarAdjuntosAlmacen", idRegistro, nombreArchivoLimpio, "BaseDatos", "Archivo cargado a hosting pero falló el UPDATE sinc = 0 en tabla " & tabla)
                                    End If
                                Else
                                    RegistrarErrorAdjunto("ExportarAdjuntosAlmacen", idRegistro, nombreArchivoLimpio, "FTPHosting", "FTPHosting.SubirArchivo devolvió False. Se mantiene sinc = 1.")
                                End If

                            Catch exRow As Exception
                                RegistrarErrorAdjunto("ExportarAdjuntosAlmacen", idRegistro, nombreArchivo, "Excepcion", "Error no controlado procesando registro: " & exRow.Message)
                            Finally
                                ' Limpieza de archivo temporal si quedó remanente
                                Try
                                    If Not String.IsNullOrWhiteSpace(nombreArchivo) Then
                                        Dim rutaTempClean As String = System.IO.Path.Combine(tempFolder, System.IO.Path.GetFileName(nombreArchivo))
                                        If System.IO.File.Exists(rutaTempClean) Then
                                            System.IO.File.Delete(rutaTempClean)
                                        End If
                                    End If
                                Catch exClean As Exception
                                End Try
                            End Try
                        Next
                    End If
                Catch exTabla As Exception
                    LogEventos.Escribir("Error al consultar la tabla " & tabla & ": " & exTabla.Message)
                End Try
            Next
        Catch ex As Exception
            LogEventos.Escribir("Error general en ExportarAdjuntosAlmacen: " & ex.Message)
        End Try

    End Sub

    ''' <summary>
    ''' Exporta al servidor central los archivos adjuntos registrados en tb_pases_salida_adjuntos con sinc = 1.
    ''' Localiza cada archivo en /TB_RECIBOS (disco local o FTP local) y lo transfiere mediante FTPHosting
    ''' a /sistema.lfmcontrol.com.mx/Assets/files/pases_salida/.
    ''' Solo tras confirmar la carga exitosa, actualiza sinc = 0.
    ''' </summary>
    Public Sub ExportarAdjuntosPasesSalida()

        If Not _servidorCentralConectado Then
            Exit Sub
        End If

        SyncLock _lockAdjuntosPasesSalida
            If _procesandoAdjuntosPasesSalida Then
                Exit Sub
            End If
            _procesandoAdjuntosPasesSalida = True
        End SyncLock

        Try
            ' 1. Consultar registros de tb_pases_salida_adjuntos con sinc = 1
            Dim query As String = "SELECT id, pase_salida_id, archivo, tipo_archivo, duracion_segundos, fchregistro, ccveusuario, sinc " & _
                                  "FROM tb_pases_salida_adjuntos WHERE sinc = 1;"
            Dim dt As DataTable = tb_Recordset_MySQL_local(query)

            If dt Is Nothing OrElse dt.Rows.Count = 0 Then
                Exit Sub
            End If

            ' Preparar credenciales para cliente FTP local en caso de requerir descarga
            Dim rawIp As String = Me.FTP_IP
            If String.IsNullOrWhiteSpace(rawIp) Then rawIp = IpServidor
            If String.IsNullOrWhiteSpace(rawIp) Then rawIp = "127.0.0.1"

            rawIp = rawIp.Replace("ftp://", "").Replace("ftps://", "").Replace("http://", "").Trim("/"c, " "c)
            If String.IsNullOrWhiteSpace(rawIp) Then rawIp = "127.0.0.1"

            Dim localFtpHost As String = "ftp://" & rawIp
            Dim localFtpUser As String = Me.FTP_USUARIO
            Dim localFtpPass As String = Me.FTP_PASSWORD

            Dim ftpLocalClient As New FtpClient(localFtpHost, localFtpUser, localFtpPass)

            ' Directorio temporal local seguro (staging) antes de enviar a FTPHosting
            Dim tempFolder As String = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HistoMedic_Temp")
            If Not System.IO.Directory.Exists(tempFolder) Then
                System.IO.Directory.CreateDirectory(tempFolder)
            End If

            Const CARPETA_DESTINO_REMOTA As String = "/sistema.lfmcontrol.com.mx/Assets/files/pases_salida/"
            Dim idsProcesados As New HashSet(Of Integer)()
            Dim ftpCentral As New FTPHosting()

            For Each row As DataRow In dt.Rows

                Dim idRegistro As Integer = 0
                If IsDBNull(row("id")) OrElse Not Integer.TryParse(row("id").ToString(), idRegistro) OrElse idRegistro <= 0 Then
                    Continue For
                End If

                ' Evitar reprocesar duplicados en el mismo ciclo
                If idsProcesados.Contains(idRegistro) Then
                    Continue For
                End If
                idsProcesados.Add(idRegistro)

                ' Validar campo archivo: si es NULL, Nothing, cadena vacía o espacios, ignorar y continuar sin registrar error
                If String.IsNullOrWhiteSpace(Convert.ToString(row("archivo"))) Then
                    Continue For
                End If

                Dim nombreArchivo As String = Convert.ToString(row("archivo")).Trim()
                Dim nombreArchivoLimpio As String = System.IO.Path.GetFileName(nombreArchivo)
                If String.IsNullOrWhiteSpace(nombreArchivoLimpio) Then
                    Continue For
                End If

                Dim rutaTemporalLocal As String = System.IO.Path.Combine(tempFolder, nombreArchivoLimpio)

                Try
                    ' 2. Descarga exclusiva desde FTP local /TB_RECIBOS/
                    Dim localizado As Boolean = False
                    Dim rutaRemotaLocal As String = localFtpHost & "/TB_RECIBOS/" & nombreArchivoLimpio
                    Try
                        Dim descargado As Boolean = ftpLocalClient.DescargarArchivo(rutaRemotaLocal, rutaTemporalLocal)
                        If descargado AndAlso System.IO.File.Exists(rutaTemporalLocal) Then
                            localizado = True
                        End If
                    Catch exDescarga As Exception
                        LogEventos.Escribir(String.Format("[Pases Salida Adjuntos] ID {0}: Error al descargar desde FTP local /TB_RECIBOS/: {1}", idRegistro, exDescarga.Message))
                        RegistrarErrorAdjunto("ExportarAdjuntosPasesSalida", idRegistro, nombreArchivoLimpio, "DescargaFTP", "Excepción al descargar de FTP local (" & rutaRemotaLocal & "): " & exDescarga.Message)
                    End Try

                    ' 3. Verificar que el archivo exista y sea accesible
                    If Not localizado OrElse Not System.IO.File.Exists(rutaTemporalLocal) Then
                        LogEventos.Escribir(String.Format("[Pases Salida Adjuntos] ID {0}: Archivo '{1}' no se pudo localizar ni descargar desde TB_RECIBOS. Se mantiene sinc = 1.", idRegistro, nombreArchivoLimpio))
                        Continue For
                    End If

                    Dim fi As New System.IO.FileInfo(rutaTemporalLocal)
                    If fi.Length = 0 Then
                        LogEventos.Escribir(String.Format("[Pases Salida Adjuntos] ID {0}: Archivo '{1}' se encuentra vacío (0 bytes). Se mantiene sinc = 1.", idRegistro, nombreArchivoLimpio))
                        Continue For
                    End If

                    ' 4 & 5. Conectar mediante FTPHosting y subir al servidor central
                    Dim subido As Boolean = False
                    Try
                        subido = ftpCentral.SubirArchivo(rutaTemporalLocal, nombreArchivoLimpio, CARPETA_DESTINO_REMOTA)
                    Catch exFtp As Exception
                        LogEventos.Escribir(String.Format("[Pases Salida Adjuntos] ID {0}: Excepción en FTPHosting al subir '{1}': {2}", idRegistro, nombreArchivoLimpio, exFtp.Message))
                        subido = False
                    End Try

                    ' 6 & 7. Solo después de confirmar el éxito de la transferencia, actualizar sinc = 0
                    If subido Then
                        Dim actualizado As Boolean = False
                        Try
                            If cx_MySQL_local.State = ConnectionState.Closed Then
                                cx_MySQL_local.Open()
                            End If

                            Using cmdUpdate As New MySqlConnector.MySqlCommand("UPDATE tb_pases_salida_adjuntos SET sinc = 0 WHERE id = @id;", cx_MySQL_local)
                                cmdUpdate.Parameters.Add("@id", MySqlConnector.MySqlDbType.Int32).Value = idRegistro
                                Dim rowsAffected As Integer = cmdUpdate.ExecuteNonQuery()
                                actualizado = (rowsAffected > 0)
                            End Using
                        Catch exUpdate As Exception
                            LogEventos.Escribir(String.Format("[Pases Salida Adjuntos] ID {0}: Archivo '{1}' subido pero error al actualizar sinc = 0 local: {2}", idRegistro, nombreArchivoLimpio, exUpdate.Message))
                        End Try

                        If actualizado Then
                            LogEventos.Escribir(String.Format("[Pases Salida Adjuntos] ID {0}: Archivo '{1}' ({2:N0} bytes) exportado exitosamente al servidor central y actualizado sinc = 0.", idRegistro, nombreArchivoLimpio, fi.Length))
                        End If
                    Else
                        LogEventos.Escribir(String.Format("[Pases Salida Adjuntos] ID {0}: Falló la carga FTP de '{1}' al servidor central. Se mantiene sinc = 1.", idRegistro, nombreArchivoLimpio))
                    End If

                Catch exArchivo As Exception
                    LogEventos.Escribir(String.Format("[Pases Salida Adjuntos] ID {0}: Error no controlado procesando archivo '{1}': {2}. Se mantiene sinc = 1.", idRegistro, nombreArchivo, exArchivo.Message))
                Finally
                    ' Limpieza de seguridad del archivo temporal local
                    Try
                        If System.IO.File.Exists(rutaTemporalLocal) Then
                            System.IO.File.Delete(rutaTemporalLocal)
                        End If
                    Catch exClean As Exception
                    End Try
                End Try

            Next

        Catch exGeneral As Exception
            LogEventos.Escribir(String.Format("[Pases Salida Adjuntos] Error general en ExportarAdjuntosPasesSalida: {0}", exGeneral.Message))
        Finally
            SyncLock _lockAdjuntosPasesSalida
                _procesandoAdjuntosPasesSalida = False
            End SyncLock
        End Try

    End Sub

#End Region

#Region "Notificaciones Cotizaciones Pendientes Compras"

    ''' <summary>
    ''' Rutina automática para notificar al personal de compras las partidas pendientes de cotizar de FLOWserve.
    ''' Ocurre de lunes a viernes (omitiendo sábados y domingos).
    ''' </summary>
    Public Sub NotificarCotizacionesPendientesFlowserve(Optional ByVal forzarEnvio As Boolean = False)
        Try
            If Not forzarEnvio Then
                If DateTime.Now.DayOfWeek = DayOfWeek.Saturday OrElse DateTime.Now.DayOfWeek = DayOfWeek.Sunday Then
                    Return
                End If
            End If

            ProcesarNotificacionCotizacionesPendientes(
                "FLOWserve",
                "frecuencia_notifica_flowserve",
                "correos_segcot_compras_flowserve",
                "fecha_ultima_notifica_flowserve",
                New Integer() {2, 3, 4, 6},
                forzarEnvio
            )
        Catch ex As Exception
            AgregarLog(500, "Error en NotificarCotizacionesPendientesFlowserve: " & ex.Message)
            LogEventos.Escribir("Error en NotificarCotizacionesPendientesFlowserve: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Rutina automática para notificar al personal de compras las partidas pendientes de cotizar de DIVERSOS.
    ''' Ocurre de lunes a viernes (omitiendo sábados y domingos).
    ''' </summary>
    Public Sub NotificarCotizacionesPendientesDiversos(Optional ByVal forzarEnvio As Boolean = False)
        Try
            If Not forzarEnvio Then
                If DateTime.Now.DayOfWeek = DayOfWeek.Saturday OrElse DateTime.Now.DayOfWeek = DayOfWeek.Sunday Then
                    Return
                End If
            End If

            ProcesarNotificacionCotizacionesPendientes(
                "DIVERSOS",
                "frecuencia_notifica_diversos",
                "correos_segcot_compras_diversos",
                "fecha_ultima_notifica_diversos",
                New Integer() {5, 6},
                forzarEnvio
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
                                                          ByVal clasificacionesIds As Integer(),
                                                          Optional ByVal forzarEnvio As Boolean = False)
        If _procesandoNotificaciones Then Return

        ' 0. Omitir sábados y domingos (salvo si es forzado manualmente para pruebas)
        If Not forzarEnvio Then
            If DateTime.Now.DayOfWeek = DayOfWeek.Saturday OrElse DateTime.Now.DayOfWeek = DayOfWeek.Sunday Then
                Return
            End If

            ' Validar que la hora actual sea a partir de las 8:30 AM
            Dim horaProgramada As New TimeSpan(8, 30, 0)
            If DateTime.Now.TimeOfDay < horaProgramada Then
                ' Aún no son las 8:30 AM del día actual, esperar a la hora programada
                Return
            End If
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
            Dim sqlConfig As String = String.Format("SELECT {0}, {1}, {2}, correos_solo_directivos FROM cat_consultorio LIMIT 1",
                                                    campoFrecuencia, campoDestinatarios, campoFechaUltima)
            Dim dtConfig As DataTable = tb_Recordset_MySQL_local(sqlConfig)
            If dtConfig Is Nothing OrElse dtConfig.Rows.Count = 0 Then
                AgregarLog(500, String.Format("[{0}] Configuración no encontrada en cat_consultorio.", tipoNotificacion))
                Return
            End If

            Dim rowConf As DataRow = dtConfig.Rows(0)

            ' Obtener correos de directivos para envío en copia (CC)
            Dim correosDirectivos As String = ""
            If dtConfig.Columns.Contains("correos_solo_directivos") AndAlso Not IsDBNull(rowConf("correos_solo_directivos")) Then
                correosDirectivos = rowConf("correos_solo_directivos").ToString().Trim()
            End If

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

            If Not forzarEnvio AndAlso fechaUltimaNotif.HasValue Then
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
                "  COALESCE(vd.codigo_partida, '') AS codigo_partida, " & _
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
                "ORDER BY cp.clasificacion, COALESCE(p.cDatGenRazonSocial, p.cDatGenNombreAbreviado, 'PROVEEDOR NO ASIGNADO'), c.folio_solicitud, cd.id;"

            cmm.CommandText = sqlPartidas
            Dim dtPartidas As New DataTable()
            Dim da As New MySqlConnector.MySqlDataAdapter(cmm)
            da.Fill(dtPartidas)

            ' 3. Enviar únicamente si existen partidas pendientes
            If dtPartidas.Rows.Count = 0 Then
                LogEventos.Escribir(String.Format("[{0}] No existen partidas pendientes de cotizar. No se envía correo.", tipoNotificacion))
                Return
            End If

            'AgregarLog(100, String.Format("[{0}] Se encontraron {1} partidas pendientes de cotizar. Generando correo HTML...", tipoNotificacion, dtPartidas.Rows.Count))

            ' 4. Generar HTML y Asunto
            Dim htmlCuerpo As String = GenerarHtmlCotizacionesPendientes(tipoNotificacion, dtPartidas, frecuenciaDias)
            Dim asunto As String = String.Format("[LFMControl] Cotizaciones Pendientes de Cotizar - {0} ({1} partidas)", tipoNotificacion, dtPartidas.Rows.Count)

            ' Guardar respaldo local del HTML generado para consulta y auditoría
            Try
                Dim rutaHtmlLocal As String = System.IO.Path.Combine(Application.StartupPath, "UltimoCotizacionesPendientes_" & tipoNotificacion & ".html")
                System.IO.File.WriteAllText(rutaHtmlLocal, htmlCuerpo, System.Text.Encoding.UTF8)
            Catch exFile As Exception
            End Try

            ' 5. Enviar correo a los destinatarios configurados con copia (CC) a directivos
            Dim enviadoExitoso As Boolean = EnviarCorreoNotificacionHTML(destinatarios, asunto, htmlCuerpo, correosDirectivos)

            If enviadoExitoso Then
                ' 6. Actualizar fecha_ultima_notifica en cat_consultorio para evitar duplicados
                Dim sqlUpdate As String = String.Format("UPDATE cat_consultorio SET {0} = NOW()", campoFechaUltima)
                Using cmmUpd As New MySqlConnector.MySqlCommand(sqlUpdate, cx_MySQL_local)
                    If cx_MySQL_local.State = ConnectionState.Closed Then cx_MySQL_local.Open()
                    cmmUpd.ExecuteNonQuery()
                End Using

                'AgregarLog(200, String.Format("[{0}] Notificación enviada con éxito a: {1}{2} ({3} partidas notificadas).", tipoNotificacion, destinatarios, If(Not String.IsNullOrWhiteSpace(correosDirectivos), " [CC: " & correosDirectivos & "]", ""), dtPartidas.Rows.Count))
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
        sb.AppendLine("    <div style=""background-color: #ffffff; border: 1px solid #cbd5e1; border-left: 6px solid #0284c7; border-radius: 8px; margin-bottom: 24px; overflow: hidden;"">")

        ' Encabezado de la tarjeta ejecutiva
        sb.AppendLine("      <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""#f8fafc"" style=""width: 100%; border-collapse: collapse; background-color: #f8fafc; border-bottom: 1px solid #e2e8f0;"">")
        sb.AppendLine("        <tr>")
        sb.AppendLine("          <td bgcolor=""#f8fafc"" style=""padding: 14px 18px; text-align: left; background-color: #f8fafc;"">")
        sb.AppendLine("            <div style=""font-size: 14px; font-weight: 800; color: #0f172a; text-transform: uppercase; letter-spacing: 0.5px;"">&#128202; ANÁLISIS EJECUTIVO</div>")
        sb.AppendLine("            <div style=""font-size: 11px; color: #64748b; margin-top: 2px;"">Diagnóstico automático y recomendaciones estratégicas orientadas a la toma de decisiones</div>")
        sb.AppendLine("          </td>")
        sb.AppendLine(String.Format("          <td bgcolor=""#f8fafc"" style=""padding: 14px 18px; text-align: right; font-size: 11px; color: #475569; background-color: #f8fafc;"">Periodo: <strong>{0}</strong></td>", DateTime.Now.ToString("dd/MM/yyyy")))
        sb.AppendLine("        </tr>")
        sb.AppendLine("      </table>")

        sb.AppendLine("      <div style=""padding: 18px;"">")

        ' Cuadrícula de Métricas Clave (4 cajas)
        Dim colorAntig As String = If(maxDiasAntig >= 10, "#b91c1c", If(maxDiasAntig >= 4, "#d97706", "#15803d"))
        sb.AppendLine("        <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""#ffffff"" style=""width: 100%; border-collapse: separate; border-spacing: 8px; margin-bottom: 16px; background-color: #ffffff;"">")
        sb.AppendLine("          <tr>")
        ' Tarjeta 1
        sb.AppendLine("            <td bgcolor=""#f8fafc"" style=""width: 25%; background-color: #f8fafc; border: 1px solid #cbd5e1; border-radius: 6px; padding: 10px 8px; vertical-align: top; text-align: center;"">")
        sb.AppendLine("              <div style=""font-size: 10px; color: #64748b; text-transform: uppercase; font-weight: 700;"">Solicitudes Pendientes</div>")
        sb.AppendLine(String.Format("              <div style=""font-size: 20px; font-weight: 800; color: #0f172a; margin-top: 4px;"">{0}</div>", totalSolicitudesCount))
        sb.AppendLine(String.Format("              <div style=""font-size: 11px; color: #475569; margin-top: 2px;"">{0} partidas en total</div>", totalPartidas))
        sb.AppendLine("            </td>")
        ' Tarjeta 2
        sb.AppendLine("            <td bgcolor=""#f8fafc"" style=""width: 25%; background-color: #f8fafc; border: 1px solid #cbd5e1; border-radius: 6px; padding: 10px 8px; vertical-align: top; text-align: center;"">")
        sb.AppendLine("              <div style=""font-size: 10px; color: #64748b; text-transform: uppercase; font-weight: 700;"">Clasificación Principal</div>")
        sb.AppendLine(String.Format("              <div style=""font-size: 13px; font-weight: 800; color: #1e3a8a; margin-top: 4px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;"">{0}</div>", System.Net.WebUtility.HtmlEncode(topClasifNombre)))
        sb.AppendLine(String.Format("              <div style=""font-size: 11px; color: #475569; margin-top: 2px;"">{0} partidas ({1}%)</div>", topClasifCount, topClasifPct))
        sb.AppendLine("            </td>")
        ' Tarjeta 3
        sb.AppendLine("            <td bgcolor=""#f8fafc"" style=""width: 25%; background-color: #f8fafc; border: 1px solid #cbd5e1; border-radius: 6px; padding: 10px 8px; vertical-align: top; text-align: center;"">")
        sb.AppendLine("              <div style=""font-size: 10px; color: #64748b; text-transform: uppercase; font-weight: 700;"">Proveedor Principal</div>")
        sb.AppendLine(String.Format("              <div style=""font-size: 13px; font-weight: 800; color: #0f172a; margin-top: 4px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;"">{0}</div>", System.Net.WebUtility.HtmlEncode(topProvNombre)))
        sb.AppendLine(String.Format("              <div style=""font-size: 11px; color: #475569; margin-top: 2px;"">{0} partidas ({1}%)</div>", topProvCount, topProvPct))
        sb.AppendLine("            </td>")
        ' Tarjeta 4
        sb.AppendLine("            <td bgcolor=""#f8fafc"" style=""width: 25%; background-color: #f8fafc; border: 1px solid #cbd5e1; border-radius: 6px; padding: 10px 8px; vertical-align: top; text-align: center;"">")
        sb.AppendLine("              <div style=""font-size: 10px; color: #64748b; text-transform: uppercase; font-weight: 700;"">Antigüedad Máxima</div>")
        sb.AppendLine(String.Format("              <div style=""font-size: 20px; font-weight: 800; color: {0}; margin-top: 4px;"">{1} días</div>", colorAntig, maxDiasAntig))
        sb.AppendLine(String.Format("              <div style=""font-size: 11px; color: #475569; margin-top: 2px;"">{0}</div>", If(Not String.IsNullOrWhiteSpace(fechaMasAntiguaStr), "Desde " & fechaMasAntiguaStr, "Sin fecha registrada")))
        sb.AppendLine("            </td>")
        sb.AppendLine("          </tr>")
        sb.AppendLine("        </table>")

        ' Hallazgos Clave de Concentración y Distribución
        sb.AppendLine("        <div style=""margin-bottom: 16px;"">")
        sb.AppendLine("          <div style=""font-size: 12px; font-weight: 700; color: #0f172a; margin-bottom: 6px;"">&#128269; CONCENTRACIONES Y TENDENCIAS CLAVE:</div>")
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

        Dim esFlowserve As Boolean = tipoNotificacion.IndexOf("flowserve", StringComparison.OrdinalIgnoreCase) >= 0
        Dim hdrBg As String = If(esFlowserve, "#1e40af", "#0f766e")
        Dim hdrGrad As String = If(esFlowserve, "linear-gradient(135deg, #1e40af 0%, #3b82f6 100%)", "linear-gradient(135deg, #0f766e 0%, #0284c7 100%)")
        Dim hdrSubColor As String = If(esFlowserve, "#dbeafe", "#ccfbf1")

        sb.AppendLine("<!DOCTYPE html>")
        sb.AppendLine("<html>")
        sb.AppendLine("<head>")
        sb.AppendLine("<meta http-equiv=""Content-Type"" content=""text/html; charset=utf-8"" />")
        sb.AppendLine("<meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />")
        sb.AppendLine("<style type=""text/css"">")
        sb.AppendLine("  body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f1f5f9; margin: 0; padding: 0; color: #1e293b; }")
        sb.AppendLine("  .wrapper-table { width: 100%; background-color: #f1f5f9; border-collapse: collapse; }")
        sb.AppendLine("  .main-card { max-width: 920px; margin: 0 auto; background-color: #ffffff; border-radius: 8px; overflow: hidden; border: 1px solid #cbd5e1; }")
        sb.AppendLine("  .header { color: #ffffff; padding: 22px 28px; text-align: left; }")
        sb.AppendLine("  .header h1 { margin: 0 0 6px 0; font-size: 20px; font-weight: 800; letter-spacing: -0.5px; color: #ffffff !important; }")
        sb.AppendLine("  .header p { margin: 0; font-size: 13px; }")
        sb.AppendLine("  .stats-bar { width: 100%; border-collapse: collapse; background-color: #f8fafc; border-bottom: 2px solid #e2e8f0; }")
        sb.AppendLine("  .stat-badge { display: inline-block; background-color: #1e40af; color: #ffffff !important; font-weight: bold; border-radius: 12px; padding: 2px 8px; font-size: 11px; margin-left: 4px; }")
        sb.AppendLine("  .content { padding: 20px 24px; background-color: #ffffff; }")
        sb.AppendLine("  .clasif-section { margin-bottom: 24px; }")
        sb.AppendLine("  .clasif-title { width: 100%; border-collapse: collapse; background-color: #eff6ff; border-left: 5px solid #2563eb; border-bottom: 1px solid #dbeafe; margin-bottom: 14px; }")
        sb.AppendLine("  .prov-section { margin-bottom: 18px; }")
        sb.AppendLine("  .prov-title { width: 100%; border-collapse: collapse; margin-top: 6px; margin-bottom: 12px; }")
        sb.AppendLine("  .solicitud-card { background: #ffffff; border: 1px solid #cbd5e1; border-radius: 6px; margin-bottom: 16px; overflow: hidden; }")
        sb.AppendLine("  .solicitud-header { width: 100%; border-collapse: collapse; background-color: #f8fafc; border-bottom: 1px solid #e2e8f0; }")
        sb.AppendLine("  .sol-title { font-size: 13px; font-weight: 700; color: #0f172a; margin-bottom: 4px; }")
        sb.AppendLine("  .sol-meta { font-size: 11px; color: #475569; line-height: 1.5; }")
        sb.AppendLine("  .sol-meta strong { color: #1e293b; }")
        sb.AppendLine("  table.items-table { width: 100%; border-collapse: collapse; font-size: 11px; text-align: left; background-color: #ffffff; }")
        sb.AppendLine("  table.items-table th { background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 8px 10px; border-bottom: 2px solid #cbd5e1; font-size: 10px; text-transform: uppercase; }")
        sb.AppendLine("  table.items-table td { padding: 8px 10px; border-bottom: 1px solid #e2e8f0; vertical-align: top; }")
        sb.AppendLine("  .tag-code { display: inline-block; background-color: #f1f5f9; border: 1px solid #cbd5e1; border-radius: 3px; padding: 1px 5px; font-family: Consolas, monospace; font-size: 11px; color: #0f172a; }")
        sb.AppendLine("  .desc-adic { font-size: 11px; color: #64748b; margin-top: 3px; font-style: italic; }")
        sb.AppendLine("  .footer { background-color: #f8fafc; border-top: 1px solid #e2e8f0; padding: 16px 24px; font-size: 11px; color: #64748b; text-align: center; }")
        sb.AppendLine("</style>")
        sb.AppendLine("</head>")
        sb.AppendLine("<body style=""margin: 0; padding: 0; background-color: #f1f5f9; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; color: #1e293b;"">")
        sb.AppendLine("<table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""#f1f5f9"" class=""wrapper-table"" style=""width: 100%; border-collapse: collapse; background-color: #f1f5f9; margin: 0; padding: 0;"">")
        sb.AppendLine("  <tr>")
        sb.AppendLine("    <td align=""center"" style=""padding: 16px 8px; background-color: #f1f5f9;"">")
        sb.AppendLine("      <!--[if (gte mso 9)|(IE)]>")
        sb.AppendLine("      <table role=""presentation"" align=""center"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""920"" style=""width: 920px;"">")
        sb.AppendLine("        <tr>")
        sb.AppendLine("          <td align=""center"" valign=""top"">")
        sb.AppendLine("      <![endif]-->")
        sb.AppendLine("      <table role=""presentation"" align=""center"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""#ffffff"" class=""main-card"" style=""max-width: 920px; width: 100%; margin: 0 auto; background-color: #ffffff; border-radius: 8px; border: 1px solid #cbd5e1; border-collapse: separate; overflow: hidden;"">")
        sb.AppendLine("        <tr>")
        sb.AppendLine("          <td align=""left"" bgcolor=""#ffffff"" style=""background-color: #ffffff; padding: 0;"">")
        sb.AppendLine("")
        sb.AppendLine("            <!-- Encabezado principal corporativo claro -->")
        sb.AppendLine(String.Format("            <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""{0}"" class=""header"" style=""width: 100%; border-collapse: collapse; background-color: {0}; background: {1};"">", hdrBg, hdrGrad))
        sb.AppendLine("              <tr>")
        sb.AppendLine(String.Format("                <td bgcolor=""{0}"" style=""padding: 22px 28px; text-align: left; background-color: {0};"">", hdrBg))
        sb.AppendLine(String.Format("                  <h1 style=""margin: 0 0 6px 0; font-size: 20px; font-weight: 800; letter-spacing: -0.5px; color: #ffffff !important; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;"">Notificación de Cotizaciones Pendientes - {0}</h1>", System.Net.WebUtility.HtmlEncode(tipoNotificacion)))
        sb.AppendLine(String.Format("                  <p style=""margin: 0; font-size: 13px; color: {0} !important; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;"">Partidas pendientes de cotizar registradas en solicitudes a proveedores &bull; Generado el {1}</p>", hdrSubColor, DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss")))
        sb.AppendLine("                </td>")
        sb.AppendLine("              </tr>")
        sb.AppendLine("            </table>")
        sb.AppendLine("")
        sb.AppendLine("            <!-- Barra de estadísticas -->")
        sb.AppendLine("            <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""#f8fafc"" class=""stats-bar"" style=""width: 100%; border-collapse: collapse; background-color: #f8fafc; border-bottom: 2px solid #e2e8f0;"">")
        sb.AppendLine("              <tr>")
        sb.AppendLine(String.Format("                <td bgcolor=""#f8fafc"" style=""padding: 12px 24px; font-size: 12px; color: #475569; vertical-align: middle; background-color: #f8fafc; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;"">Solicitudes con pendientes: <span class=""stat-badge"" style=""display: inline-block; background-color: #1e40af; color: #ffffff !important; font-weight: bold; border-radius: 12px; padding: 2px 8px; font-size: 11px; margin-left: 4px;"">{0}</span></td>", totalSolicitudes.Count))
        sb.AppendLine(String.Format("                <td bgcolor=""#f8fafc"" style=""padding: 12px 10px; font-size: 12px; color: #475569; vertical-align: middle; text-align: center; background-color: #f8fafc; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;"">Total Partidas Pendientes: <span class=""stat-badge"" style=""display: inline-block; background-color: #1e40af; color: #ffffff !important; font-weight: bold; border-radius: 12px; padding: 2px 8px; font-size: 11px; margin-left: 4px;"">{0}</span></td>", dt.Rows.Count))
        sb.AppendLine(String.Format("                <td bgcolor=""#f8fafc"" style=""padding: 12px 24px; font-size: 12px; color: #475569; vertical-align: middle; text-align: right; background-color: #f8fafc; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;"">Frecuencia programada: <strong style=""color: #1e293b;"">Cada {0} día(s)</strong></td>", frecuenciaDias))
        sb.AppendLine("              </tr>")
        sb.AppendLine("            </table>")
        sb.AppendLine("")
        sb.AppendLine("            <div class=""content"" style=""padding: 20px 24px; background-color: #ffffff;"">")
        sb.AppendLine("")
        sb.AppendLine("              <!-- Generar e insertar el Análisis Ejecutivo antes del detalle -->")
        sb.Append(GenerarResumenEjecutivoHtml(tipoNotificacion, dt, totalSolicitudes.Count))
        sb.AppendLine("")
        sb.AppendLine("              <div style=""margin-top: 10px; margin-bottom: 16px; font-size: 15px; font-weight: 700; color: #1e3a8a; border-bottom: 2px solid #e2e8f0; padding-bottom: 6px;"">&#128203; DETALLE DE SOLICITUDES Y PARTIDAS PENDIENTES</div>")

        ' Nivel 1: Clasificación de Proyecto
        For Each clasif In clasificaciones
            Dim clasifCurrent As String = clasif
            Dim rowsClasif As DataRow() = dt.Select(String.Format("clasificacion_nombre = '{0}'", clasifCurrent.Replace("'", "''")))

            sb.AppendLine("              <div class=""clasif-section"" style=""margin-bottom: 24px;"">")
            sb.AppendLine("                <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""#eff6ff"" class=""clasif-title"" style=""width: 100%; border-collapse: collapse; background-color: #eff6ff; border-left: 5px solid #2563eb; border-bottom: 1px solid #dbeafe; margin-bottom: 14px;"">")
            sb.AppendLine(String.Format("                  <tr><td bgcolor=""#eff6ff"" style=""padding: 9px 14px; font-size: 13px; font-weight: 700; color: #1e40af; text-transform: uppercase; letter-spacing: 0.3px; background-color: #eff6ff;"">&#9658; Clasificación: {0} &nbsp;<span style=""font-size: 11px; font-weight: normal; color: #64748b;"">({1} partidas)</span></td></tr>", System.Net.WebUtility.HtmlEncode(clasifCurrent), rowsClasif.Length))
            sb.AppendLine("                </table>")

            ' Nivel 2: Proveedores dentro de la clasificación
            Dim proveedores As New List(Of String)()
            For Each r In rowsClasif
                Dim pNom As String = If(Not IsDBNull(r("proveedor_nombre")) AndAlso Not String.IsNullOrWhiteSpace(r("proveedor_nombre").ToString()), r("proveedor_nombre").ToString().Trim(), "PROVEEDOR NO ASIGNADO")
                If Not proveedores.Contains(pNom) Then
                    proveedores.Add(pNom)
                End If
            Next
            ' Ordenar alfabéticamente dejando "PROVEEDOR NO ASIGNADO" al final
            proveedores = proveedores.OrderBy(Function(p) If(p.Equals("PROVEEDOR NO ASIGNADO", StringComparison.OrdinalIgnoreCase), "ZZZZZZZZ", p)).ToList()

            For Each prov In proveedores
                Dim provCurrent As String = prov
                Dim rowsProv As DataRow() = rowsClasif.Where(Function(r)
                                                                 Dim pNom As String = If(Not IsDBNull(r("proveedor_nombre")) AndAlso Not String.IsNullOrWhiteSpace(r("proveedor_nombre").ToString()), r("proveedor_nombre").ToString().Trim(), "PROVEEDOR NO ASIGNADO")
                                                                 Return pNom.Equals(provCurrent, StringComparison.OrdinalIgnoreCase)
                                                             End Function).ToArray()

                ' Conteo de solicitudes únicas para este proveedor en esta clasificación
                Dim cotizacionIdsProv As New List(Of String)()
                For Each r In rowsProv
                    Dim cotIdStr As String = r("cotizacion_id").ToString()
                    If Not cotizacionIdsProv.Contains(cotIdStr) Then
                        cotizacionIdsProv.Add(cotIdStr)
                    End If
                Next

                Dim cantSolsProv As Integer = cotizacionIdsProv.Count
                Dim cantPartidasProv As Integer = rowsProv.Length
                Dim txtSols As String = If(cantSolsProv = 1, "1 solicitud", cantSolsProv & " solicitudes")
                Dim txtPartidas As String = If(cantPartidasProv = 1, "1 partida", cantPartidasProv & " partidas")

                Dim esSinProv As Boolean = provCurrent.Equals("PROVEEDOR NO ASIGNADO", StringComparison.OrdinalIgnoreCase)
                Dim provBorderColor As String = If(esSinProv, "#d97706", "#0284c7")
                Dim provBgColor As String = If(esSinProv, "#fffbeb", "#f8fafc")
                Dim provTextColor As String = If(esSinProv, "#92400e", "#0f172a")
                Dim provBorderAll As String = If(esSinProv, "#fcd34d", "#cbd5e1")

                sb.AppendLine("                <div class=""prov-section"" style=""margin-bottom: 18px;"">")
                sb.AppendLine(String.Format("                  <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""{0}"" class=""prov-title"" style=""width: 100%; border-collapse: collapse; background-color: {0}; border: 1px solid {1}; border-left: 4px solid {2}; margin-top: 6px; margin-bottom: 12px; border-radius: 4px;"">", provBgColor, provBorderAll, provBorderColor))
                sb.AppendLine(String.Format("                    <tr><td bgcolor=""{0}"" style=""padding: 7px 12px; font-size: 12px; font-weight: 700; color: {1}; background-color: {0}; text-transform: uppercase; letter-spacing: 0.3px;"">&#9670; Proveedor: {2} &nbsp;<span style=""font-size: 11px; font-weight: normal; color: #475569; text-transform: none;"">({3} &bull; {4})</span></td></tr>", provBgColor, provTextColor, System.Net.WebUtility.HtmlEncode(provCurrent), txtSols, txtPartidas))
                sb.AppendLine("                  </table>")

                ' Nivel 3: Solicitudes de Cotización de este Proveedor
                For Each cotIdStr In cotizacionIdsProv
                    Dim idCurrent As String = cotIdStr
                    Dim rowsCot As DataRow() = rowsProv.Where(Function(r) r("cotizacion_id").ToString() = idCurrent) _
                                                          .OrderBy(Function(r) If(IsDBNull(r("codigo_partida")), "", r("codigo_partida").ToString()), StringComparer.OrdinalIgnoreCase) _
                                                          .ThenBy(Function(r) Convert.ToInt64(r("detalle_id"))) _
                                                          .ToArray()
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

                    sb.AppendLine("                  <div class=""solicitud-card"" style=""background: #ffffff; border: 1px solid #cbd5e1; border-radius: 6px; margin-bottom: 14px; overflow: hidden;"">")
                    sb.AppendLine("                    <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""#f8fafc"" class=""solicitud-header"" style=""width: 100%; border-collapse: collapse; background-color: #f8fafc; border-bottom: 1px solid #e2e8f0;"">")
                    sb.AppendLine("                      <tr><td bgcolor=""#f8fafc"" style=""padding: 10px 14px; background-color: #f8fafc;"">")
                    sb.AppendLine(String.Format("                        <div class=""sol-title"" style=""font-size: 13px; font-weight: 700; color: #0f172a; margin-bottom: 4px;"">Solicitud: <span style=""font-family: Consolas, monospace; color: #1e40af;"">{0}</span>{1} &bull; Proveedor: <strong style=""color: #0f172a;"">{2}</strong></div>",
                                                System.Net.WebUtility.HtmlEncode(folioSol),
                                                If(Not String.IsNullOrWhiteSpace(folioCot), " (Cotiz: " & System.Net.WebUtility.HtmlEncode(folioCot) & ")", ""),
                                                System.Net.WebUtility.HtmlEncode(provNom)))

                    sb.AppendLine("                        <div class=""sol-meta"" style=""font-size: 11px; color: #475569; line-height: 1.5;"">")
                    sb.AppendLine(String.Format("                          <strong>Proyecto:</strong> {0} &bull; <strong>Fecha Solicitud:</strong> {1}",
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

                    sb.AppendLine("                        </div>")
                    sb.AppendLine("                      </td></tr>")
                    sb.AppendLine("                    </table>")

                    ' Nivel 4: Tabla de Partidas Pendientes
                    sb.AppendLine("                    <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""#ffffff"" class=""items-table"" style=""width: 100%; border-collapse: collapse; font-size: 11px; text-align: left; background-color: #ffffff;"">")
                    sb.AppendLine("                      <thead>")
                    sb.AppendLine("                        <tr bgcolor=""#f1f5f9"">")
                    sb.AppendLine("                          <th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 7px 10px; border-bottom: 2px solid #cbd5e1; font-size: 10px; text-transform: uppercase; width: 10%; text-align: center;"">Partida</th>")
                    sb.AppendLine("                          <th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 7px 10px; border-bottom: 2px solid #cbd5e1; font-size: 10px; text-transform: uppercase; width: 12%; text-align: center;"">Cantidad</th>")
                    sb.AppendLine("                          <th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 7px 10px; border-bottom: 2px solid #cbd5e1; font-size: 10px; text-transform: uppercase; width: 14%;"">Cód. Prov.</th>")
                    sb.AppendLine("                          <th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 7px 10px; border-bottom: 2px solid #cbd5e1; font-size: 10px; text-transform: uppercase; width: 16%;"">No. Parte</th>")
                    sb.AppendLine("                          <th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 7px 10px; border-bottom: 2px solid #cbd5e1; font-size: 10px; text-transform: uppercase; width: 48%;"">Descripción / Concepto</th>")
                    sb.AppendLine("                        </tr>")
                    sb.AppendLine("                      </thead>")
                    sb.AppendLine("                      <tbody>")

                    Dim idxPartida As Integer = 0
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

                        Dim rowBg As String = If(idxPartida Mod 2 = 0, "#ffffff", "#f8fafc")
                        sb.AppendLine(String.Format("                        <tr bgcolor=""{0}"" style=""background-color: {0};"">", rowBg))
                        sb.AppendLine(String.Format("                          <td bgcolor=""{0}"" style=""text-align: center; font-weight: bold; color: #0f172a; padding: 7px 10px; border-bottom: 1px solid #e2e8f0; background-color: {0}; font-family: Consolas, monospace;"">{1}</td>", rowBg, System.Net.WebUtility.HtmlEncode(partidaNum)))
                        sb.AppendLine(String.Format("                          <td bgcolor=""{0}"" style=""text-align: center; font-weight: bold; color: #1e293b; padding: 7px 10px; border-bottom: 1px solid #e2e8f0; background-color: {0};"">{1:N2} {2}</td>", rowBg, cantVal, System.Net.WebUtility.HtmlEncode(unidadStr)))
                        sb.AppendLine(String.Format("                          <td bgcolor=""{0}"" style=""padding: 7px 10px; border-bottom: 1px solid #e2e8f0; background-color: {0};"">{1}</td>", rowBg, If(Not String.IsNullOrWhiteSpace(codProv), "<span class=""tag-code"" style=""display: inline-block; background-color: #f1f5f9; border: 1px solid #cbd5e1; border-radius: 3px; padding: 1px 5px; font-family: Consolas, monospace; font-size: 11px; color: #0f172a;"">" & System.Net.WebUtility.HtmlEncode(codProv) & "</span>", "-")))
                        sb.AppendLine(String.Format("                          <td bgcolor=""{0}"" style=""padding: 7px 10px; border-bottom: 1px solid #e2e8f0; background-color: {0};"">{1}</td>", rowBg, If(Not String.IsNullOrWhiteSpace(numParte), "<span class=""tag-code"" style=""display: inline-block; background-color: #f1f5f9; border: 1px solid #cbd5e1; border-radius: 3px; padding: 1px 5px; font-family: Consolas, monospace; font-size: 11px; color: #0f172a;"">" & System.Net.WebUtility.HtmlEncode(numParte) & "</span>", "-")))

                        sb.Append(String.Format("                          <td bgcolor=""{0}"" style=""padding: 7px 10px; border-bottom: 1px solid #e2e8f0; background-color: {0}; color: #1e293b;"">", rowBg))
                        sb.Append(System.Net.WebUtility.HtmlEncode(descProv))
                        If Not String.IsNullOrWhiteSpace(descAdic) AndAlso Not descAdic.Equals(descProv, StringComparison.OrdinalIgnoreCase) Then
                            sb.Append(String.Format("<div class=""desc-adic"" style=""font-size: 11px; color: #64748b; margin-top: 3px; font-style: italic;"">{0}</div>", System.Net.WebUtility.HtmlEncode(descAdic)))
                        End If
                        sb.AppendLine("</td>")
                        sb.AppendLine("                        </tr>")
                        idxPartida += 1
                    Next

                    sb.AppendLine("                      </tbody>")
                    sb.AppendLine("                    </table>")
                    sb.AppendLine("                  </div>")
                Next

                sb.AppendLine("                </div>")
            Next

            sb.AppendLine("              </div>")
        Next

        sb.AppendLine("            </div>")
        sb.AppendLine("")
        sb.AppendLine("            <!-- Pie de página institucional -->")
        sb.AppendLine("            <div class=""footer"" style=""background-color: #f8fafc; border-top: 1px solid #e2e8f0; padding: 16px 24px; font-size: 11px; color: #64748b; text-align: center;"">")
        sb.AppendLine("              <p style=""margin: 0 0 4px 0; font-weight: 700; color: #475569;"">LFM RPA Robot &bull; Notificación Automática de Partidas Pendientes de Cotizar. Powered by HistoMedic.</p>")
        sb.AppendLine("              <p style=""margin: 0; color: #64748b;"">Este mensaje fue generado automáticamente según la frecuencia programada en configuración general. Por favor no responder a este correo.</p>")
        sb.AppendLine("            </div>")
        sb.AppendLine("")
        sb.AppendLine("          </td>")
        sb.AppendLine("        </tr>")
        sb.AppendLine("      </table>")
        sb.AppendLine("      <!--[if (gte mso 9)|(IE)]>")
        sb.AppendLine("          </td>")
        sb.AppendLine("        </tr>")
        sb.AppendLine("      </table>")
        sb.AppendLine("      <![endif]-->")
        sb.AppendLine("    </td>")
        sb.AppendLine("  </tr>")
        sb.AppendLine("</table>")
        sb.AppendLine("</body>")
        sb.AppendLine("</html>")
        Return sb.ToString()
    End Function

    ''' <summary>
    ''' Envía un correo electrónico en formato HTML a múltiples destinatarios usando Chilkat MailMan.
    ''' No muestra cuadros de diálogo interactivos (MsgBox) y registra cualquier error en logs.
    ''' </summary>
    Private Function EnviarCorreoNotificacionHTML(ByVal destinatarios As String, ByVal asunto As String, ByVal cuerpoHtml As String, Optional ByVal correosCopia As String = "") As Boolean
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

            ' Agregar correos en copia (CC) si fueron especificados
            If Not String.IsNullOrWhiteSpace(correosCopia) Then
                Dim listaCC As String() = correosCopia.Split(separadores, StringSplitOptions.RemoveEmptyEntries)
                For Each ccRaw As String In listaCC
                    Dim ccLimpio As String = ccRaw.Trim()
                    If Not String.IsNullOrWhiteSpace(ccLimpio) AndAlso EsDireccionCorreoValida(ccLimpio) Then
                        email.AddCC("", ccLimpio)
                    End If
                Next
            End If

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
        Public Property TotalCotizacionesClienteEnviadas As Integer
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

        Public ReadOnly Property EsColocado As Boolean
            Get
                Return EstatusId >= 6 AndAlso Not EsCanceladoODeclinado
            End Get
        End Property

        Public ReadOnly Property EsCotizado As Boolean
            Get
                If EsCanceladoODeclinado Then Return False
                Return (EstatusId = 5 OrElse (TotalCotizacionesClienteEnviadas > 0 AndAlso EstatusId < 6))
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

        ' 1. Omitir sábados y domingos (salvo si es forzado manualmente para pruebas)
        If Not forzarEnvio Then
            If DateTime.Now.DayOfWeek = DayOfWeek.Saturday OrElse DateTime.Now.DayOfWeek = DayOfWeek.Sunday Then
                Return
            End If

            ' 2. Validar horario de envío diario (a partir de las 08:00 AM)
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

            ' Validar en BD si hoy ya se registró el snapshot (evita envíos duplicados ante reinicios del robot)
            If Not forzarEnvio Then
                Dim sqlCheckHoy As String = "SELECT COUNT(*) FROM tb_informe_proyectos_historico WHERE fecha = CURDATE()"
                Dim dtCheck As DataTable = tb_Recordset_MySQL_local(sqlCheckHoy)
                If dtCheck IsNot Nothing AndAlso dtCheck.Rows.Count > 0 AndAlso Convert.ToInt32(dtCheck.Rows(0)(0)) > 0 Then
                    _fechaUltimoEnvioInformeProyectos = DateTime.Now
                    Return
                End If
            End If

            ' 3. Obtener correos destinatarios del campo correos_solo_directivos en cat_consultorio
            Dim sqlConsultorio As String = "SELECT correos_solo_directivos FROM cat_consultorio LIMIT 1"
            Dim dtConsultorio As DataTable = tb_Recordset_MySQL_local(sqlConsultorio)
            If dtConsultorio Is Nothing OrElse dtConsultorio.Rows.Count = 0 OrElse IsDBNull(dtConsultorio.Rows(0)("correos_solo_directivos")) Then
                AgregarLog(500, "[Informe Ejecutivo Proyectos] No se encontró configuración en cat_consultorio.correos_solo_directivos.")
                Return
            End If

            Dim destinatarios As String = dtConsultorio.Rows(0)("correos_solo_directivos").ToString().Trim()
            If String.IsNullOrWhiteSpace(destinatarios) Then
                AgregarLog(500, "[Informe Ejecutivo Proyectos] Omitido: cat_consultorio.correos_solo_directivos está vacío.")
                Return
            End If

            ' 4. Consultar y evaluar proyectos generados a partir del 24 de agosto de 2026 hasta etapa 7
            Dim listaProyectos As List(Of ItemProyectoInforme) = ConsultarProyectosSeguimiento()
            If listaProyectos.Count = 0 Then
                LogEventos.Escribir("[Informe Ejecutivo Proyectos] No se encontraron proyectos que cumplan con los filtros.")
                Return
            End If

            'AgregarLog(100, String.Format("[Informe Ejecutivo Proyectos] Procesando {0} proyectos (a partir del 24/08/2026). Generando informe HTML...", listaProyectos.Count))

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

                'AgregarLog(200, String.Format("[Informe Ejecutivo Proyectos] Notificación diaria enviada con éxito a: {0} ({1} proyectos reportados).", destinatarios, listaProyectos.Count))
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
            "  (SELECT COUNT(*) FROM tb_ventas_cotizacion_cliente cc WHERE cc.venta_id = v.id AND cc.enviado = 1 AND cc.activo = 1) AS total_cotizaciones_cliente_enviadas, " & _
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
            "ORDER BY cliente_nombre, vendedor_nombre, cp.clasificacion, v.id DESC;"

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
            item.TotalCotizacionesClienteEnviadas = If(Not IsDBNull(r("total_cotizaciones_cliente_enviadas")), Convert.ToInt32(r("total_cotizaciones_cliente_enviadas")), 0)
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

        ' Regla Especial: Estatus 5 - COTIZACION CLIENTE ELABORADA (PEDIDO COTIZADO)
        ' El semáforo se evalúa según los días restantes para recibir respuesta del cliente:
        ' - ROJO si faltan 10 días o menos (o ya venció).
        ' - AMARILLO si faltan 20 días o menos (y más de 10 días).
        ' - VERDE si faltan más de 20 días.
        If item.EstatusId = 5 Then
            If item.FechaCompromiso.HasValue Then
                Dim diasRestantes As Integer = CInt(Math.Floor((item.FechaCompromiso.Value.Date - DateTime.Now.Date).TotalDays))
                If diasRestantes <= 10 Then
                    item.Semaforo = "ROJO"
                    If diasRestantes < 0 Then
                        item.MotivoPrioridad = String.Format("Vigencia/Respuesta de cotización vencida hace {0} día(s) ({1:dd/MM/yy})", Math.Abs(diasRestantes), item.FechaCompromiso.Value)
                        item.IndicadorRiesgo = "CRÍTICO - Compromiso Vencido"
                    ElseIf diasRestantes = 0 Then
                        item.MotivoPrioridad = String.Format("Vigencia/Respuesta de cotización vence HOY ({0:dd/MM/yy})", item.FechaCompromiso.Value)
                        item.IndicadorRiesgo = "CRÍTICO - Vence Hoy"
                    Else
                        item.MotivoPrioridad = String.Format("Faltan {0} día(s) para recibir respuesta del cliente (Vigencia: {1:dd/MM/yy})", diasRestantes, item.FechaCompromiso.Value)
                        item.IndicadorRiesgo = "CRÍTICO - Plazo Próximo a Vencer"
                    End If
                ElseIf diasRestantes <= 20 Then
                    item.Semaforo = "AMARILLO"
                    item.MotivoPrioridad = String.Format("Faltan {0} días para recibir respuesta del cliente (Vigencia: {1:dd/MM/yy})", diasRestantes, item.FechaCompromiso.Value)
                    item.IndicadorRiesgo = "ADVERTENCIA - Seguimiento Requerido"
                Else
                    item.Semaforo = "VERDE"
                    item.MotivoPrioridad = String.Format("Cotización a cliente en tiempo (Faltan {0} días, Vigencia: {1:dd/MM/yy})", diasRestantes, item.FechaCompromiso.Value)
                    item.IndicadorRiesgo = "NORMAL - En Plazo"
                End If
            Else
                item.Semaforo = "VERDE"
                item.MotivoPrioridad = "Cotización elaborada al cliente en tiempo"
                item.IndicadorRiesgo = "NORMAL"
            End If
            Return
        End If

        ' Regla Especial: Estatus 7 - ORDEN COMPRA PROVEEDOR (PEDIDO ELABORADO)
        ' El semáforo es exclusivamente VERDE o ROJO según la fecha compromiso:
        ' - VERDE si la fecha actual es antes de la fecha compromiso.
        ' - ROJO si la fecha actual es igual o mayor a la fecha compromiso.
        If item.EstatusId = 7 Then
            If item.FechaCompromiso.HasValue Then
                If DateTime.Now.Date < item.FechaCompromiso.Value.Date Then
                    item.Semaforo = "VERDE"
                    item.MotivoPrioridad = String.Format("Entrega de proveedor en tiempo (Compromiso: {0:dd/MM/yy})", item.FechaCompromiso.Value)
                    item.IndicadorRiesgo = "NORMAL - En Plazo"
                Else
                    item.Semaforo = "ROJO"
                    If DateTime.Now.Date = item.FechaCompromiso.Value.Date Then
                        item.MotivoPrioridad = String.Format("Fecha compromiso ({0}) vence HOY", item.TipoFechaCompromiso)
                        item.IndicadorRiesgo = "CRÍTICO - Compromiso Vence Hoy"
                    Else
                        Dim diasVencidos As Integer = CInt(Math.Floor((DateTime.Now.Date - item.FechaCompromiso.Value.Date).TotalDays))
                        item.MotivoPrioridad = String.Format("Fecha compromiso ({0}) vencida hace {1} día(s)", item.TipoFechaCompromiso, diasVencidos)
                        item.IndicadorRiesgo = "CRÍTICO - Compromiso Vencido"
                    End If
                End If
            Else
                item.Semaforo = "VERDE"
                item.MotivoPrioridad = "Orden de compra colocada a proveedor en proceso"
                item.IndicadorRiesgo = "NORMAL"
            End If
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
        sb.AppendLine("  body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f1f5f9; margin: 0; padding: 0; color: #1e293b; }")
        sb.AppendLine("  .wrapper-table { width: 100%; background-color: #f1f5f9; border-collapse: collapse; }")
        sb.AppendLine("  .main-card { max-width: 960px; margin: 0 auto; background-color: #ffffff; border-radius: 8px; overflow: hidden; border: 1px solid #cbd5e1; }")
        sb.AppendLine("  .main-header { background-color: #1e40af; background: linear-gradient(135deg, #1e40af 0%, #3b82f6 100%); color: #ffffff; padding: 22px 28px; text-align: left; }")
        sb.AppendLine("  .main-header h1 { margin: 0 0 6px 0; font-size: 20px; font-weight: 800; letter-spacing: -0.5px; color: #ffffff !important; }")
        sb.AppendLine("  .main-header p { margin: 0; font-size: 13px; color: #dbeafe !important; }")
        sb.AppendLine("  .kpi-banner { width: 100%; border-collapse: collapse; background-color: #f8fafc; border-bottom: 2px solid #e2e8f0; text-align: center; }")
        sb.AppendLine("  .kpi-cell { padding: 12px 10px; border-right: 1px solid #e2e8f0; }")
        sb.AppendLine("  .kpi-label { font-size: 10px; text-transform: uppercase; font-weight: 700; letter-spacing: 0.5px; color: #64748b; margin-bottom: 4px; }")
        sb.AppendLine("  .kpi-value { font-size: 18px; font-weight: 800; }")
        sb.AppendLine("  .kpi-val-tot { color: #1e293b; }")
        sb.AppendLine("  .kpi-val-grn { color: #15803d; }")
        sb.AppendLine("  .kpi-val-yel { color: #b45309; }")
        sb.AppendLine("  .kpi-val-red { color: #b91c1c; }")
        sb.AppendLine("  .kpi-val-mto { color: #0284c7; font-size: 14px; }")
        sb.AppendLine("  .container { padding: 20px 24px; background-color: #ffffff; }")
        sb.AppendLine("  .sec-heading { font-size: 15px; font-weight: 700; color: #1e3a8a; margin: 24px 0 12px 0; padding-bottom: 6px; border-bottom: 2px solid #e2e8f0; }")
        sb.AppendLine("  .badge-v { display: inline-block; background-color: #dcfce7; color: #15803d; border: 1px solid #86efac; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px; }")
        sb.AppendLine("  .badge-a { display: inline-block; background-color: #fef9c3; color: #a16207; border: 1px solid #fde047; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px; }")
        sb.AppendLine("  .badge-r { display: inline-block; background-color: #fee2e2; color: #b91c1c; border: 1px solid #fca5a5; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px; }")
        sb.AppendLine("  table.data-table { width: 100%; border-collapse: collapse; font-size: 12px; margin-bottom: 18px; background-color: #ffffff; }")
        sb.AppendLine("  table.data-table th { background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 8px 10px; border-bottom: 2px solid #cbd5e1; text-transform: uppercase; font-size: 11px; text-align: left; }")
        sb.AppendLine("  table.data-table td { padding: 8px 10px; border-bottom: 1px solid #e2e8f0; vertical-align: middle; }")
        sb.AppendLine("  .clasif-block { margin-bottom: 24px; border: 1px solid #cbd5e1; border-radius: 6px; overflow: hidden; background-color: #ffffff; }")
        sb.AppendLine("  .clasif-bar { background-color: #eff6ff; color: #1e40af; border-left: 5px solid #2563eb; border-bottom: 1px solid #dbeafe; padding: 9px 14px; font-weight: 700; font-size: 13px; text-transform: uppercase; letter-spacing: 0.3px; }")
        sb.AppendLine("  .card-prio { border-left: 5px solid #dc2626; background-color: #fff1f2; padding: 12px 16px; margin-bottom: 12px; border-radius: 4px; border-top: 1px solid #fecdd3; border-right: 1px solid #fecdd3; border-bottom: 1px solid #fecdd3; }")
        sb.AppendLine("  .card-prio-title { font-size: 13px; font-weight: 700; color: #991b1b; margin-bottom: 4px; }")
        sb.AppendLine("  .card-prio-meta { font-size: 11px; color: #475569; line-height: 1.5; }")
        sb.AppendLine("  .analisis-box { background-color: #f0fdf4; border: 1px solid #bbf7d0; border-left: 5px solid #16a34a; border-radius: 6px; padding: 16px 20px; margin-bottom: 22px; font-size: 12px; line-height: 1.6; color: #1e293b; }")
        sb.AppendLine("  .analisis-box ul { margin: 6px 0 0 18px; padding: 0; }")
        sb.AppendLine("  .analisis-box li { margin-bottom: 6px; }")
        sb.AppendLine("  .footer { background-color: #f8fafc; border-top: 1px solid #e2e8f0; padding: 16px 24px; font-size: 11px; color: #64748b; text-align: center; }")
        sb.AppendLine("</style>")
        sb.AppendLine("</head>")
        sb.AppendLine("<body style=""margin: 0; padding: 0; background-color: #f1f5f9; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; color: #1e293b;"">")
        sb.AppendLine("<table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""#f1f5f9"" class=""wrapper-table"" style=""width: 100%; border-collapse: collapse; background-color: #f1f5f9; margin: 0; padding: 0;"">")
        sb.AppendLine("  <tr>")
        sb.AppendLine("    <td align=""center"" style=""padding: 16px 8px; background-color: #f1f5f9;"">")
        sb.AppendLine("      <!--[if (gte mso 9)|(IE)]>")
        sb.AppendLine("      <table role=""presentation"" align=""center"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""960"" style=""width: 960px;"">")
        sb.AppendLine("        <tr>")
        sb.AppendLine("          <td align=""center"" valign=""top"">")
        sb.AppendLine("      <![endif]-->")
        sb.AppendLine("      <table role=""presentation"" align=""center"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""#ffffff"" class=""main-card"" style=""max-width: 960px; width: 100%; margin: 0 auto; background-color: #ffffff; border-radius: 8px; border: 1px solid #cbd5e1; border-collapse: separate; overflow: hidden;"">")
        sb.AppendLine("        <tr>")
        sb.AppendLine("          <td align=""left"" bgcolor=""#ffffff"" style=""background-color: #ffffff; padding: 0;"">")
        sb.AppendLine("")
        sb.AppendLine("            <!-- 1. Header principal corporativo claro -->")
        sb.AppendLine("            <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""#1e40af"" class=""main-header"" style=""width: 100%; border-collapse: collapse; background-color: #1e40af; background: linear-gradient(135deg, #1e40af 0%, #3b82f6 100%);"">")
        sb.AppendLine("              <tr>")
        sb.AppendLine("                <td bgcolor=""#1e40af"" style=""padding: 22px 28px; background-color: #1e40af; text-align: left;"">")
        sb.AppendLine("                  <h1 style=""margin: 0 0 6px 0; font-size: 20px; font-weight: 800; letter-spacing: -0.5px; color: #ffffff !important; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;"">Informe Ejecutivo de Seguimiento de Proyectos</h1>")
        sb.AppendLine(String.Format("                  <p style=""margin: 0; font-size: 13px; color: #dbeafe !important; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;"">Cartera Activa desde el 24 de Agosto de 2026 (Etapas 1 a 7) &bull; Emitido el {0}</p>", DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss")))
        sb.AppendLine("                </td>")
        sb.AppendLine("              </tr>")
        sb.AppendLine("            </table>")
        sb.AppendLine("")
        sb.AppendLine("            <!-- 2. Banner superior de Indicadores Clave (KPI) -->")
        sb.AppendLine("            <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""#f8fafc"" class=""kpi-banner"" style=""width: 100%; border-collapse: collapse; background-color: #f8fafc; border-bottom: 2px solid #e2e8f0; text-align: center;"">")
        sb.AppendLine("              <tr>")
        sb.AppendLine(String.Format("                <td class=""kpi-cell"" bgcolor=""#f8fafc"" style=""padding: 12px 10px; border-right: 1px solid #e2e8f0; text-align: center;""><div class=""kpi-label"" style=""font-size: 10px; text-transform: uppercase; font-weight: 700; letter-spacing: 0.5px; color: #64748b; margin-bottom: 4px;"">Total Proyectos</div><div class=""kpi-value kpi-val-tot"" style=""font-size: 18px; font-weight: 800; color: #1e293b;"">{0}</div></td>", totalProyectos))
        sb.AppendLine(String.Format("                <td class=""kpi-cell"" bgcolor=""#f8fafc"" style=""padding: 12px 10px; border-right: 1px solid #e2e8f0; text-align: center;""><div class=""kpi-label"" style=""font-size: 10px; text-transform: uppercase; font-weight: 700; letter-spacing: 0.5px; color: #64748b; margin-bottom: 4px;"">Verdes</div><div class=""kpi-value kpi-val-grn"" style=""font-size: 18px; font-weight: 800; color: #15803d;"">{0} <span style=""font-size: 11px; font-weight: normal;"">({1}%)</span></div></td>", verdesCount, pctVerdes))
        sb.AppendLine(String.Format("                <td class=""kpi-cell"" bgcolor=""#f8fafc"" style=""padding: 12px 10px; border-right: 1px solid #e2e8f0; text-align: center;""><div class=""kpi-label"" style=""font-size: 10px; text-transform: uppercase; font-weight: 700; letter-spacing: 0.5px; color: #64748b; margin-bottom: 4px;"">Amarillos</div><div class=""kpi-value kpi-val-yel"" style=""font-size: 18px; font-weight: 800; color: #b45309;"">{0} <span style=""font-size: 11px; font-weight: normal;"">({1}%)</span></div></td>", amarillosCount, pctAmarillos))
        sb.AppendLine(String.Format("                <td class=""kpi-cell"" bgcolor=""#f8fafc"" style=""padding: 12px 10px; border-right: 1px solid #e2e8f0; text-align: center;""><div class=""kpi-label"" style=""font-size: 10px; text-transform: uppercase; font-weight: 700; letter-spacing: 0.5px; color: #64748b; margin-bottom: 4px;"">Rojos</div><div class=""kpi-value kpi-val-red"" style=""font-size: 18px; font-weight: 800; color: #b91c1c;"">{0} <span style=""font-size: 11px; font-weight: normal;"">({1}%)</span></div></td>", rojosCount, pctRojos))
        sb.AppendLine(String.Format("                <td class=""kpi-cell"" bgcolor=""#f8fafc"" style=""padding: 12px 10px; border-right: 1px solid #e2e8f0; text-align: center;""><div class=""kpi-label"" style=""font-size: 10px; text-transform: uppercase; font-weight: 700; letter-spacing: 0.5px; color: #64748b; margin-bottom: 4px;"">Pendientes Críticos</div><div class=""kpi-value kpi-val-red"" style=""font-size: 18px; font-weight: 800; color: #b91c1c;"">{0}</div></td>", rojosCount))
        sb.AppendLine(String.Format("                <td class=""kpi-cell"" bgcolor=""#f8fafc"" style=""padding: 12px 10px; border-right: none; text-align: center;""><div class=""kpi-label"" style=""font-size: 10px; text-transform: uppercase; font-weight: 700; letter-spacing: 0.5px; color: #64748b; margin-bottom: 4px;"">Monto en Cartera</div><div class=""kpi-value kpi-val-mto"" style=""font-size: 14px; font-weight: 800; color: #0284c7;"">${0:N0} USD<br/><span style=""font-size: 11px; color: #475569; font-weight: 600;"">${1:N0} MXN</span></div></td>", totalMontoUSD, totalMontoMXN))
        sb.AppendLine("              </tr>")
        sb.AppendLine("            </table>")
        sb.AppendLine("")
        sb.AppendLine("            <div class=""container"" style=""padding: 20px 24px; background-color: #ffffff;"">")
        sb.AppendLine("")
        sb.AppendLine("              <!-- 3. Resumen Ejecutivo -->")
        sb.Append(GenerarResumenEjecutivoProyectosHtml(proyectos))
        sb.AppendLine("")
        sb.AppendLine("              <!-- 4. Pendientes Identificados y Contabilizados -->")
        sb.Append(GenerarPendientesProyectosHtml(proyectos))
        sb.AppendLine("")
        sb.AppendLine("              <!-- 5. Antigüedad y Proyectos Destacados -->")
        sb.Append(GenerarAntiguedadProyectosHtml(proyectos))
        sb.AppendLine("")
        sb.AppendLine("              <!-- 6. Análisis Ejecutivo Automático Inteligente -->")
        sb.Append(GenerarAnalisisEjecutivoTextoHtml(proyectos, dtSnapAyer))
        sb.AppendLine("")
        sb.AppendLine("              <!-- 7. Prioridades de Atención Inmediata -->")
        sb.Append(GenerarPrioridadesAtencionHtml(proyectos))
        sb.AppendLine("")
        sb.AppendLine("              <!-- 8. Comparativo Histórico -->")
        sb.Append(GenerarComparativoHistoricoHtml(proyectos, dtSnapAyer, dtSnap7Dias))
        sb.AppendLine("")
        sb.AppendLine("              <!-- 9. Detalle Completo de Proyectos (Agrupado por Cliente) -->")
        sb.Append(GenerarDetalleProyectosHtml(proyectos))
        sb.AppendLine("")
        sb.AppendLine("              <!-- 10. Apartado Exclusivo para Proyectos DECLINADOS -->")
        sb.Append(GenerarProyectosDeclinadosHtml(proyectos))
        sb.AppendLine("")
        sb.AppendLine("            </div>")
        sb.AppendLine("")
        sb.AppendLine("            <!-- Pie de página institucional -->")
        sb.AppendLine("            <div class=""footer"" style=""background-color: #f8fafc; border-top: 1px solid #e2e8f0; padding: 16px 24px; font-size: 11px; color: #64748b; text-align: center;"">")
        sb.AppendLine("              <p style=""margin: 0 0 4px 0; font-weight: 700; color: #475569;"">HistoMedic LFM RPA Robot &bull; Informe Ejecutivo Diario de Seguimiento de Proyectos</p>")
        sb.AppendLine("              <p style=""margin: 0; color: #64748b;"">Generado automáticamente para el cuerpo directivo y jefes de área. Datos auditados en tiempo real.</p>")
        sb.AppendLine("            </div>")
        sb.AppendLine("")
        sb.AppendLine("          </td>")
        sb.AppendLine("        </tr>")
        sb.AppendLine("      </table>")
        sb.AppendLine("      <!--[if (gte mso 9)|(IE)]>")
        sb.AppendLine("          </td>")
        sb.AppendLine("        </tr>")
        sb.AppendLine("      </table>")
        sb.AppendLine("      <![endif]-->")
        sb.AppendLine("    </td>")
        sb.AppendLine("  </tr>")
        sb.AppendLine("</table>")
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

        sb.AppendLine("    <div class=""sec-heading"" style=""font-size: 15px; font-weight: 700; color: #1e3a8a; margin: 20px 0 12px 0; padding-bottom: 6px; border-bottom: 2px solid #e2e8f0;"">&#128202; 1. Resumen Ejecutivo</div>")

        ' Cuadrícula de tarjetas de resumen
        sb.AppendLine("    <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""#ffffff"" style=""width: 100%; border-collapse: separate; border-spacing: 6px; margin-bottom: 16px; background-color: #ffffff;"">")
        sb.AppendLine("      <tr>")
        sb.AppendLine(String.Format("        <td bgcolor=""#f8fafc"" style=""width: 16.6%; background-color: #f8fafc; border: 1px solid #cbd5e1; border-radius: 4px; padding: 10px 6px; text-align: center;""><div style=""font-size: 10px; color: #64748b; font-weight: 700; text-transform: uppercase;"">Proyectos Activos</div><div style=""font-size: 16px; font-weight: 800; color: #0f172a;"">{0}</div></td>", totalActivos))
        sb.AppendLine(String.Format("        <td bgcolor=""#f8fafc"" style=""width: 16.6%; background-color: #f8fafc; border: 1px solid #cbd5e1; border-radius: 4px; padding: 10px 6px; text-align: center;""><div style=""font-size: 10px; color: #64748b; font-weight: 700; text-transform: uppercase;"">Nuevos Hoy</div><div style=""font-size: 16px; font-weight: 800; color: #2563eb;"">{0}</div></td>", nuevosHoy))
        sb.AppendLine(String.Format("        <td bgcolor=""#f8fafc"" style=""width: 16.6%; background-color: #f8fafc; border: 1px solid #cbd5e1; border-radius: 4px; padding: 10px 6px; text-align: center;""><div style=""font-size: 10px; color: #64748b; font-weight: 700; text-transform: uppercase;"">Actualizados Hoy</div><div style=""font-size: 16px; font-weight: 800; color: #059669;"">{0}</div></td>", actualizadosHoy))
        sb.AppendLine(String.Format("        <td bgcolor=""#f8fafc"" style=""width: 16.6%; background-color: #f8fafc; border: 1px solid #cbd5e1; border-radius: 4px; padding: 10px 6px; text-align: center;""><div style=""font-size: 10px; color: #64748b; font-weight: 700; text-transform: uppercase;"">Sin Movimiento (&#8805;4d)</div><div style=""font-size: 16px; font-weight: 800; color: #d97706;"">{0}</div></td>", sinMovimiento))
        sb.AppendLine(String.Format("        <td bgcolor=""#f8fafc"" style=""width: 16.6%; background-color: #f8fafc; border: 1px solid #cbd5e1; border-radius: 4px; padding: 10px 6px; text-align: center;""><div style=""font-size: 10px; color: #64748b; font-weight: 700; text-transform: uppercase;"">En Colocación (OC)</div><div style=""font-size: 16px; font-weight: 800; color: #16a34a;"">{0}</div></td>", cerradosGanados))
        sb.AppendLine(String.Format("        <td bgcolor=""#f8fafc"" style=""width: 16.6%; background-color: #f8fafc; border: 1px solid #cbd5e1; border-radius: 4px; padding: 10px 6px; text-align: center;""><div style=""font-size: 10px; color: #64748b; font-weight: 700; text-transform: uppercase;"">Cancelados / Declinados</div><div style=""font-size: 16px; font-weight: 800; color: #dc2626;"">{0} <span style=""font-size: 10px; font-weight: normal; color: #991b1b;"">({1} canc. / {2} decl.)</span></div></td>", canceladosDeclinados, totalCancelados, totalDeclinados))
        sb.AppendLine("      </tr>")
        sb.AppendLine("    </table>")

        ' Distribución Semáforo
        sb.AppendLine("    <div style=""margin-bottom: 16px; padding: 10px 16px; background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 6px; font-size: 12px;"">")
        sb.AppendLine(String.Format("      <strong style=""color: #334155;"">Estado de Salud del Semáforo:</strong> &nbsp; " & _
                                    "<span class=""badge-v"" style=""display: inline-block; background-color: #dcfce7; color: #15803d !important; border: 1px solid #86efac; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px;"">&#9679; VERDES: {0} ({1}%)</span> &nbsp; " & _
                                    "<span class=""badge-a"" style=""display: inline-block; background-color: #fef9c3; color: #a16207 !important; border: 1px solid #fde047; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px;"">&#9679; AMARILLOS: {2} ({3}%)</span> &nbsp; " & _
                                    "<span class=""badge-r"" style=""display: inline-block; background-color: #fee2e2; color: #b91c1c !important; border: 1px solid #fca5a5; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px;"">&#9679; ROJOS: {4} ({5}%)</span>",
                                    verdesCount, pctV, amarillosCount, pctA, rojosCount, pctR))
        sb.AppendLine("    </div>")

        ' Resumen por Clasificación y por Vendedor en 2 columnas
        sb.AppendLine("    <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""#ffffff"" style=""width: 100%; border-collapse: collapse; margin-bottom: 15px; background-color: #ffffff;"">")
        sb.AppendLine("      <tr style=""vertical-align: top;"">")

        ' Columna 1: Por Clasificación
        sb.AppendLine("        <td bgcolor=""#ffffff"" style=""width: 49%; padding-right: 1%; vertical-align: top; background-color: #ffffff;"">")
        sb.AppendLine("          <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""#ffffff"" class=""data-table"" style=""width: 100%; border-collapse: collapse; font-size: 11px; margin-bottom: 0; background-color: #ffffff;"">")
        sb.AppendLine("            <thead>")
        sb.AppendLine("              <tr bgcolor=""#f1f5f9""><th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 7px 8px; border-bottom: 2px solid #cbd5e1; font-size: 11px; text-align: left;"">Clasificación</th><th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 7px 8px; border-bottom: 2px solid #cbd5e1; font-size: 11px; text-align: center;"">Proy.</th><th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 7px 8px; border-bottom: 2px solid #cbd5e1; font-size: 11px; text-align: center;"">Declinados</th><th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 7px 8px; border-bottom: 2px solid #cbd5e1; font-size: 11px; text-align: right;"">Monto USD</th><th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 7px 8px; border-bottom: 2px solid #cbd5e1; font-size: 11px; text-align: right;"">Monto MXN</th></tr>")
        sb.AppendLine("            </thead>")
        sb.AppendLine("            <tbody>")
        Dim clasifs = proyectos.GroupBy(Function(p) p.ClasificacionNombre).OrderByDescending(Function(g) g.Count)
        Dim idxClasif As Integer = 0
        For Each g In clasifs
            Dim mtoUSD As Double = g.Where(Function(p) p.MonedaSiglas.Equals("USD", StringComparison.OrdinalIgnoreCase)).Sum(Function(p) p.TotalMonto)
            Dim mtoMXN As Double = g.Where(Function(p) Not p.MonedaSiglas.Equals("USD", StringComparison.OrdinalIgnoreCase)).Sum(Function(p) p.TotalMonto)
            Dim declinadosGrp As Integer = g.Where(Function(p) p.EsDeclinado).Count()
            Dim declinadosStr As String = If(declinadosGrp > 0, String.Format("<span style=""color: #dc2626; font-weight: 700;"">{0}</span>", declinadosGrp), "<span style=""color: #94a3b8;"">0</span>")
            Dim rowBg As String = If(idxClasif Mod 2 = 0, "#ffffff", "#f8fafc")
            sb.AppendLine(String.Format("              <tr bgcolor=""{0}"" style=""background-color: {0};""><td bgcolor=""{0}"" style=""padding: 6px 8px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: {0};""><strong>{1}</strong></td><td bgcolor=""{0}"" style=""text-align: center; padding: 6px 8px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: {0};"">{2}</td><td bgcolor=""{0}"" style=""text-align: center; padding: 6px 8px; border-bottom: 1px solid #e2e8f0; background-color: {0};"">{3}</td><td bgcolor=""{0}"" style=""text-align: right; padding: 6px 8px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: {0};"">${4:N2}</td><td bgcolor=""{0}"" style=""text-align: right; padding: 6px 8px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: {0};"">${5:N2}</td></tr>",
                                        rowBg, System.Net.WebUtility.HtmlEncode(g.Key), g.Count, declinadosStr, mtoUSD, mtoMXN))
            idxClasif += 1
        Next
        sb.AppendLine("            </tbody>")
        sb.AppendLine("          </table>")
        sb.AppendLine("        </td>")

        ' Columna 2: Por Vendedor
        sb.AppendLine("        <td bgcolor=""#ffffff"" style=""width: 49%; padding-left: 1%; vertical-align: top; background-color: #ffffff;"">")
        sb.AppendLine("          <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""#ffffff"" class=""data-table"" style=""width: 100%; border-collapse: collapse; font-size: 11px; margin-bottom: 0; background-color: #ffffff;"">")
        sb.AppendLine("            <thead>")
        sb.AppendLine("              <tr bgcolor=""#f1f5f9""><th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 7px 8px; border-bottom: 2px solid #cbd5e1; font-size: 11px; text-align: left;"">Vendedor</th><th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 7px 8px; border-bottom: 2px solid #cbd5e1; font-size: 11px; text-align: center;"">Proy.</th><th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 7px 8px; border-bottom: 2px solid #cbd5e1; font-size: 11px; text-align: center;"">Declinados</th><th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 7px 8px; border-bottom: 2px solid #cbd5e1; font-size: 11px; text-align: center;"">Cotizados</th><th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 7px 8px; border-bottom: 2px solid #cbd5e1; font-size: 11px; text-align: center;"">Colocados</th><th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 7px 8px; border-bottom: 2px solid #cbd5e1; font-size: 11px; text-align: right;"">Monto USD</th><th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 7px 8px; border-bottom: 2px solid #cbd5e1; font-size: 11px; text-align: right;"">Monto MXN</th></tr>")
        sb.AppendLine("            </thead>")
        sb.AppendLine("            <tbody>")
        Dim vends = proyectos.GroupBy(Function(p) p.VendedorNombre).OrderByDescending(Function(g) g.Count)
        Dim idxVend As Integer = 0
        For Each g In vends
            Dim mtoUSD As Double = g.Where(Function(p) p.MonedaSiglas.Equals("USD", StringComparison.OrdinalIgnoreCase)).Sum(Function(p) p.TotalMonto)
            Dim mtoMXN As Double = g.Where(Function(p) Not p.MonedaSiglas.Equals("USD", StringComparison.OrdinalIgnoreCase)).Sum(Function(p) p.TotalMonto)
            Dim declinadosVend As Integer = g.Where(Function(p) p.EsDeclinado).Count()
            Dim declinadosVendStr As String = If(declinadosVend > 0, String.Format("<span style=""color: #dc2626; font-weight: 700;"">{0}</span>", declinadosVend), "<span style=""color: #94a3b8;"">0</span>")
            Dim cotizadosVend As Integer = g.Where(Function(p) p.EsCotizado).Count()
            Dim cotizadosVendStr As String = If(cotizadosVend > 0, String.Format("<span style=""color: #2563eb; font-weight: 700;"">{0}</span>", cotizadosVend), "<span style=""color: #94a3b8;"">0</span>")
            Dim colocadosVend As Integer = g.Where(Function(p) p.EsColocado).Count()
            Dim colocadosVendStr As String = If(colocadosVend > 0, String.Format("<span style=""color: #16a34a; font-weight: 700;"">{0}</span>", colocadosVend), "<span style=""color: #94a3b8;"">0</span>")
            Dim rowBg As String = If(idxVend Mod 2 = 0, "#ffffff", "#f8fafc")
            sb.AppendLine(String.Format("              <tr bgcolor=""{0}"" style=""background-color: {0};""><td bgcolor=""{0}"" style=""padding: 6px 8px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: {0};""><strong>{1}</strong></td><td bgcolor=""{0}"" style=""text-align: center; padding: 6px 8px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: {0};"">{2}</td><td bgcolor=""{0}"" style=""text-align: center; padding: 6px 8px; border-bottom: 1px solid #e2e8f0; background-color: {0};"">{3}</td><td bgcolor=""{0}"" style=""text-align: center; padding: 6px 8px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: {0};"">{4}</td><td bgcolor=""{0}"" style=""text-align: center; padding: 6px 8px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: {0};"">{5}</td><td bgcolor=""{0}"" style=""text-align: right; padding: 6px 8px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: {0};"">${6:N2}</td><td bgcolor=""{0}"" style=""text-align: right; padding: 6px 8px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: {0};"">${7:N2}</td></tr>",
                                        rowBg, System.Net.WebUtility.HtmlEncode(g.Key), g.Count, declinadosVendStr, cotizadosVendStr, colocadosVendStr, mtoUSD, mtoMXN))
            idxVend += 1
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

        sb.AppendLine("    <div class=""sec-heading"" style=""font-size: 15px; font-weight: 700; color: #1e3a8a; margin: 20px 0 12px 0; padding-bottom: 6px; border-bottom: 2px solid #e2e8f0;"">&#9888;&#65039; 2. Balance y Conteo de Pendientes</div>")
        sb.AppendLine("    <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""#ffffff"" class=""data-table"" style=""width: 100%; border-collapse: collapse; font-size: 12px; margin-bottom: 18px; background-color: #ffffff;"">")
        sb.AppendLine("      <thead>")
        sb.AppendLine("        <tr bgcolor=""#f1f5f9"">")
        sb.AppendLine("          <th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 8px 10px; border-bottom: 2px solid #cbd5e1; text-align: left; font-size: 11px;"">Categoría de Pendiente Detectado</th>")
        sb.AppendLine("          <th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 8px 10px; border-bottom: 2px solid #cbd5e1; text-align: center; font-size: 11px;"">Proyectos</th>")
        sb.AppendLine("          <th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 8px 10px; border-bottom: 2px solid #cbd5e1; text-align: left; font-size: 11px;"">Impacto Operativo / Comercial</th>")
        sb.AppendLine("          <th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 8px 10px; border-bottom: 2px solid #cbd5e1; text-align: center; font-size: 11px;"">Acción Requerida</th>")
        sb.AppendLine("        </tr>")
        sb.AppendLine("      </thead>")
        sb.AppendLine("      <tbody>")
        sb.AppendLine(String.Format("        <tr bgcolor=""#ffffff"" style=""background-color: #ffffff;""><td bgcolor=""#ffffff"" style=""padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: #ffffff;""><strong>Pendientes de Orden de Compra del Cliente</strong></td><td bgcolor=""#ffffff"" style=""text-align: center; font-weight: bold; color: #b45309; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; background-color: #ffffff;"">{0}</td><td bgcolor=""#ffffff"" style=""padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #334155; background-color: #ffffff;"">Cotizaciones en poder del cliente sin decisión formal de compra</td><td bgcolor=""#ffffff"" style=""text-align: center; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: #ffffff;"">Cierre comercial</td></tr>", pendOCCliente))
        sb.AppendLine(String.Format("        <tr bgcolor=""#f8fafc"" style=""background-color: #f8fafc;""><td bgcolor=""#f8fafc"" style=""padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: #f8fafc;""><strong>Pendientes de Elaborar Cotización Interna</strong></td><td bgcolor=""#f8fafc"" style=""text-align: center; font-weight: bold; color: #b45309; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; background-color: #f8fafc;"">{0}</td><td bgcolor=""#f8fafc"" style=""padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #334155; background-color: #f8fafc;"">Proyectos cotizados que no se ha enviado cotización interna</td><td bgcolor=""#f8fafc"" style=""text-align: center; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: #f8fafc;"">Departamento de Compras</td></tr>", pendCotInterna))
        sb.AppendLine(String.Format("        <tr bgcolor=""#ffffff"" style=""background-color: #ffffff;""><td bgcolor=""#ffffff"" style=""padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: #ffffff;""><strong>En Proceso de Cotización de Proveedor</strong></td><td bgcolor=""#ffffff"" style=""text-align: center; font-weight: bold; color: #1e293b; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; background-color: #ffffff;"">{0}</td><td bgcolor=""#ffffff"" style=""padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #334155; background-color: #ffffff;"">Solicitudes enviadas a fabricantes en espera de precio y tiempo entrega</td><td bgcolor=""#ffffff"" style=""text-align: center; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: #ffffff;"">Seguimiento compras</td></tr>", procesoCotProv))
        sb.AppendLine(String.Format("        <tr bgcolor=""#f8fafc"" style=""background-color: #f8fafc;""><td bgcolor=""#f8fafc"" style=""padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: #f8fafc;""><strong>Pendientes de Enviar Cotización al Cliente</strong></td><td bgcolor=""#f8fafc"" style=""text-align: center; font-weight: bold; color: #b91c1c; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; background-color: #f8fafc;"">{0}</td><td bgcolor=""#f8fafc"" style=""padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #334155; background-color: #f8fafc;"">Cotización interna lista, pendiente vendedor elabore cotización a cliente.</td><td bgcolor=""#f8fafc"" style=""text-align: center; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: #f8fafc;"">Envío inmediato</td></tr>", pendEnviarCotCli))
        sb.AppendLine(String.Format("        <tr bgcolor=""#ffffff"" style=""background-color: #ffffff;""><td bgcolor=""#ffffff"" style=""padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: #ffffff;""><strong>Pendientes de Respuesta / Confirmación del Cliente</strong></td><td bgcolor=""#ffffff"" style=""text-align: center; font-weight: bold; color: #1e293b; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; background-color: #ffffff;"">{0}</td><td bgcolor=""#ffffff"" style=""padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #334155; background-color: #ffffff;"">Propuesta técnica-económica entregada al cliente</td><td bgcolor=""#ffffff"" style=""text-align: center; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: #ffffff;"">Llamada de seguimiento</td></tr>", pendRespCliente))
        sb.AppendLine(String.Format("        <tr bgcolor=""#f8fafc"" style=""background-color: #f8fafc;""><td bgcolor=""#f8fafc"" style=""padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: #f8fafc;""><strong>Pendientes de Notificar a Compras</strong></td><td bgcolor=""#f8fafc"" style=""text-align: center; font-weight: bold; color: #1e293b; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; background-color: #f8fafc;"">{0}</td><td bgcolor=""#f8fafc"" style=""padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #334155; background-color: #f8fafc;"">Oportunidades en fase inicial que el vendedor aun no ha notificado a Compras</td><td bgcolor=""#f8fafc"" style=""text-align: center; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: #f8fafc;"">Levantamiento de datos</td></tr>", pendInfo))
        sb.AppendLine(String.Format("        <tr bgcolor=""#fef2f2"" style=""background-color: #fef2f2;""><td bgcolor=""#fef2f2"" style=""padding: 8px 10px; border-bottom: 1px solid #fca5a5; color: #991b1b; background-color: #fef2f2;""><strong style=""color: #b91c1c;"">Proyectos con Fecha Compromiso Vencida</strong></td><td bgcolor=""#fef2f2"" style=""text-align: center; font-weight: 800; color: #b91c1c; padding: 8px 10px; border-bottom: 1px solid #fca5a5; background-color: #fef2f2;"">{0}</td><td bgcolor=""#fef2f2"" style=""color: #991b1b; padding: 8px 10px; border-bottom: 1px solid #fca5a5; background-color: #fef2f2;"">Compromiso de entrega o vigencia superado; alto riesgo de penalización o pérdida</td><td bgcolor=""#fef2f2"" style=""text-align: center; font-weight: bold; color: #b91c1c; padding: 8px 10px; border-bottom: 1px solid #fca5a5; background-color: #fef2f2;"">Intervención Urgente</td></tr>", fchCompVencida))
        sb.AppendLine(String.Format("        <tr bgcolor=""#fffbeb"" style=""background-color: #fffbeb;""><td bgcolor=""#fffbeb"" style=""padding: 8px 10px; border-bottom: 1px solid #fde68a; color: #92400e; background-color: #fffbeb;""><strong>Proyectos Sin Movimiento (&#8805; 4 días)</strong></td><td bgcolor=""#fffbeb"" style=""text-align: center; font-weight: bold; color: #d97706; padding: 8px 10px; border-bottom: 1px solid #fde68a; background-color: #fffbeb;"">{0}</td><td bgcolor=""#fffbeb"" style=""color: #92400e; padding: 8px 10px; border-bottom: 1px solid #fde68a; background-color: #fffbeb;"">Proyectos estancados que requieren reactivación y actualización en bitácora</td><td bgcolor=""#fffbeb"" style=""text-align: center; padding: 8px 10px; border-bottom: 1px solid #fde68a; color: #92400e; background-color: #fffbeb;"">Revisión de estatus</td></tr>", sinMov4d))
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

        sb.AppendLine("    <div class=""sec-heading"" style=""font-size: 15px; font-weight: 700; color: #1e3a8a; margin: 20px 0 12px 0; padding-bottom: 6px; border-bottom: 2px solid #e2e8f0;"">&#9201; 3. Análisis de Antigüedad de Inactividad</div>")
        sb.AppendLine("    <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""#ffffff"" style=""width: 100%; border-collapse: separate; border-spacing: 6px; margin-bottom: 12px; background-color: #ffffff;"">")
        sb.AppendLine("      <tr>")
        sb.AppendLine(String.Format("        <td bgcolor=""#f0fdf4"" style=""width: 25%; background-color: #f0fdf4; border: 1px solid #bbf7d0; border-radius: 4px; padding: 10px 6px; text-align: center;""><div style=""font-size: 11px; font-weight: 700; color: #15803d;"">0 a 3 Días (Al día)</div><div style=""font-size: 18px; font-weight: 800; color: #16a34a;"">{0} proy.</div></td>", r0_3))
        sb.AppendLine(String.Format("        <td bgcolor=""#fefce8"" style=""width: 25%; background-color: #fefce8; border: 1px solid #fef08a; border-radius: 4px; padding: 10px 6px; text-align: center;""><div style=""font-size: 11px; font-weight: 700; color: #854d0e;"">4 a 7 Días (Seguimiento)</div><div style=""font-size: 18px; font-weight: 800; color: #ca8a04;"">{0} proy.</div></td>", r4_7))
        sb.AppendLine(String.Format("        <td bgcolor=""#fff7ed"" style=""width: 25%; background-color: #fff7ed; border: 1px solid #fed7aa; border-radius: 4px; padding: 10px 6px; text-align: center;""><div style=""font-size: 11px; font-weight: 700; color: #9a3412;"">8 a 15 Días (Atención)</div><div style=""font-size: 18px; font-weight: 800; color: #ea580c;"">{0} proy.</div></td>", r8_15))
        sb.AppendLine(String.Format("        <td bgcolor=""#fef2f2"" style=""width: 25%; background-color: #fef2f2; border: 1px solid #fecaca; border-radius: 4px; padding: 10px 6px; text-align: center;""><div style=""font-size: 11px; font-weight: 700; color: #991b1b;"">Más de 15 Días (Crítico)</div><div style=""font-size: 18px; font-weight: 800; color: #dc2626;"">{0} proy.</div></td>", r16mas))
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

        sb.AppendLine("    <div class=""sec-heading"" style=""font-size: 15px; font-weight: 700; color: #1e3a8a; margin: 20px 0 12px 0; padding-bottom: 6px; border-bottom: 2px solid #e2e8f0;"">&#128065; 4. Análisis Ejecutivo Inteligente</div>")
        sb.AppendLine("    <div class=""analisis-box"" style=""background-color: #f0fdf4; border: 1px solid #bbf7d0; border-left: 5px solid #16a34a; border-radius: 6px; padding: 14px 18px; margin-bottom: 20px; font-size: 12px; line-height: 1.6; color: #1e293b;"">")
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

        ' Filtrar solo aquellos proyectos de mayor impacto económico (>2500 USD o >= 50,000 MXN)
        ' y que requieran "Seguimiento comercial con cliente para cierre de venta y recepción de OC":
        ' 1. Semáforo ROJO primero, luego AMARILLO, luego VERDE
        ' 2. Compromiso vencido o próximo
        ' 3. Días sin movimiento descendente
        ' 4. Monto económico descendente
        Dim prioritarios = proyectos.Where(Function(p) Not p.EsCanceladoODeclinado AndAlso _
                                            (p.EstatusId = 5 OrElse (p.ProximaAccion IsNot Nothing AndAlso p.ProximaAccion.IndexOf("cierre de venta y recepción de OC", StringComparison.OrdinalIgnoreCase) >= 0)) AndAlso _
                                            ((p.MonedaSiglas.Equals("USD", StringComparison.OrdinalIgnoreCase) AndAlso p.TotalMonto > 2500.0) OrElse _
                                             (p.MonedaSiglas.Equals("MXN", StringComparison.OrdinalIgnoreCase) AndAlso p.TotalMonto >= 50000.0))) _
                                    .OrderBy(Function(p) If(p.Semaforo = "ROJO", 0, If(p.Semaforo = "AMARILLO", 1, 2))) _
                                    .ThenBy(Function(p) If(p.DiasParaCompromiso.HasValue, p.DiasParaCompromiso.Value, 9999)) _
                                    .ThenByDescending(Function(p) p.DiasSinMovimiento) _
                                    .ThenByDescending(Function(p) p.TotalMonto) _
                                    .ToList()

        sb.AppendLine("    <div class=""sec-heading"" style=""font-size: 15px; font-weight: 700; color: #1e3a8a; margin: 20px 0 12px 0; padding-bottom: 6px; border-bottom: 2px solid #e2e8f0;"">&#127919; 5. Prioridades de atención inmediata (proyectos de alto impacto >2500 USD o >= 50,000 mxn) pendientes de OC Cliente</div>")
        sb.AppendLine("    <p style=""font-size: 12px; color: #64748b; margin: -6px 0 14px 0;"">Listado de proyectos de mayor impacto económico (> $2,500 USD o &gt;= $50,000 MXN) que demandan seguimiento comercial con cliente para cierre de venta y recepción de Orden de Compra.</p>")

        If prioritarios.Count = 0 Then
            sb.AppendLine("    <div style=""font-size: 12px; color: #166534; background-color: #dcfce7; border: 1px solid #86efac; padding: 12px; border-radius: 6px; margin-bottom: 15px;"">&#10004; No se registran proyectos con monto &gt; $2,500 USD o &gt;= $50,000 MXN pendientes de Orden de Compra de Cliente.</div>")
        End If

        For Each p In prioritarios
            Dim badgeClass As String = If(p.Semaforo = "ROJO", "badge-r", If(p.Semaforo = "AMARILLO", "badge-a", "badge-v"))
            Dim badgeStyle As String = If(p.Semaforo = "ROJO",
                "display: inline-block; background-color: #fee2e2; color: #b91c1c !important; border: 1px solid #fca5a5; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px;",
                If(p.Semaforo = "AMARILLO",
                    "display: inline-block; background-color: #fef9c3; color: #a16207 !important; border: 1px solid #fde047; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px;",
                    "display: inline-block; background-color: #dcfce7; color: #15803d !important; border: 1px solid #86efac; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px;"))
            Dim borderCol As String = If(p.Semaforo = "ROJO", "#dc2626", If(p.Semaforo = "AMARILLO", "#d97706", "#16a34a"))
            Dim bgCol As String = If(p.Semaforo = "ROJO", "#fff1f2", If(p.Semaforo = "AMARILLO", "#fffbeb", "#f0fdf4"))

            ' Badge de días restantes para recibir respuesta del cliente (Semaforizado: <=20 días amarillo, <=10 días rojo)
            Dim badgeDiasStr As String = ""
            If p.DiasParaCompromiso.HasValue Then
                Dim dRest As Integer = p.DiasParaCompromiso.Value
                Dim bgDias As String
                Dim colDias As String
                Dim borderDias As String
                Dim txtDias As String

                If dRest < 0 Then
                    bgDias = "#fee2e2"
                    colDias = "#b91c1c"
                    borderDias = "#fca5a5"
                    txtDias = String.Format("&#9888; Vencido hace {0} día(s)", Math.Abs(dRest))
                ElseIf dRest = 0 Then
                    bgDias = "#fee2e2"
                    colDias = "#b91c1c"
                    borderDias = "#fca5a5"
                    txtDias = "&#9888; Vence HOY para respuesta"
                ElseIf dRest = 1 Then
                    bgDias = "#fee2e2"
                    colDias = "#b91c1c"
                    borderDias = "#fca5a5"
                    txtDias = "&#9203; Falta 1 día para respuesta"
                ElseIf dRest <= 10 Then
                    bgDias = "#fee2e2"
                    colDias = "#b91c1c"
                    borderDias = "#fca5a5"
                    txtDias = String.Format("&#9203; Faltan {0} días para respuesta", dRest)
                ElseIf dRest <= 20 Then
                    bgDias = "#fef9c3"
                    colDias = "#a16207"
                    borderDias = "#fde047"
                    txtDias = String.Format("&#9203; Faltan {0} días para respuesta", dRest)
                Else
                    bgDias = "#dcfce7"
                    colDias = "#15803d"
                    borderDias = "#86efac"
                    txtDias = String.Format("&#9203; Faltan {0} días para respuesta", dRest)
                End If

                badgeDiasStr = String.Format(" &nbsp;<span style=""display: inline-block; background-color: {0}; color: {1} !important; border: 1px solid {2}; padding: 2px 8px; border-radius: 10px; font-weight: 700; font-size: 11px; vertical-align: middle;"">{3}</span>",
                                             bgDias, colDias, borderDias, txtDias)
            End If

            sb.AppendLine(String.Format("    <div class=""card-prio"" style=""border-left: 5px solid {0}; background-color: {1}; padding: 12px 16px; margin-bottom: 12px; border-radius: 4px; border-top: 1px solid #e2e8f0; border-right: 1px solid #e2e8f0; border-bottom: 1px solid #e2e8f0;"">", borderCol, bgCol))
            sb.AppendLine(String.Format("      <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""{0}"" style=""width: 100%; border-collapse: collapse; background-color: {0};"">", bgCol))
            sb.AppendLine("        <tr>")
            sb.AppendLine(String.Format("          <td style=""font-size: 13px; font-weight: 700; color: #0f172a;""><span class=""{0}"" style=""{1}"">&#9679; {2}</span> &nbsp; Folio: <span style=""font-family: Consolas, monospace;"">{3}</span> &bull; {4}{5}</td>",
                                        badgeClass, badgeStyle, p.Semaforo, p.ProyectoId, System.Net.WebUtility.HtmlEncode(p.ClienteNombre), badgeDiasStr))
            sb.AppendLine(String.Format("          <td style=""text-align: right; font-weight: 800; font-size: 13px; color: #0f172a; white-space: nowrap;"">{0:C2} {1}</td>", p.TotalMonto, p.MonedaSiglas))
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

        sb.AppendLine("    <div class=""sec-heading"" style=""font-size: 15px; font-weight: 700; color: #1e3a8a; margin: 20px 0 12px 0; padding-bottom: 6px; border-bottom: 2px solid #e2e8f0;"">&#128200; 6. Comparativo Histórico de Evolución</div>")

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

        sb.AppendLine("    <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""#ffffff"" class=""data-table"" style=""width: 100%; border-collapse: collapse; font-size: 12px; margin-bottom: 18px; background-color: #ffffff;"">")
        sb.AppendLine("      <thead>")
        sb.AppendLine("        <tr bgcolor=""#f1f5f9"">")
        sb.AppendLine("          <th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 8px 10px; border-bottom: 2px solid #cbd5e1; text-align: left; font-size: 11px;"">Indicador Ejecutivo</th>")
        sb.AppendLine("          <th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 8px 10px; border-bottom: 2px solid #cbd5e1; text-align: center; font-size: 11px;"">Hoy</th>")
        sb.AppendLine("          <th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 8px 10px; border-bottom: 2px solid #cbd5e1; text-align: center; font-size: 11px;"">Día Anterior</th>")
        sb.AppendLine("          <th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 8px 10px; border-bottom: 2px solid #cbd5e1; text-align: center; font-size: 11px;"">Hace 7 Días</th>")
        sb.AppendLine("          <th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 8px 10px; border-bottom: 2px solid #cbd5e1; text-align: center; font-size: 11px;"">Tendencia</th>")
        sb.AppendLine("        </tr>")
        sb.AppendLine("      </thead>")
        sb.AppendLine("      <tbody>")
        sb.AppendLine(String.Format("        <tr bgcolor=""#ffffff"" style=""background-color: #ffffff;""><td bgcolor=""#ffffff"" style=""padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: #ffffff;""><strong>Proyectos Activos</strong></td><td bgcolor=""#ffffff"" style=""text-align: center; font-weight: 700; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: #ffffff;"">{0}</td><td bgcolor=""#ffffff"" style=""text-align: center; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: #ffffff;"">{1}</td><td bgcolor=""#ffffff"" style=""text-align: center; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: #ffffff;"">{2}</td><td bgcolor=""#ffffff"" style=""text-align: center; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; background-color: #ffffff;"">{3}</td></tr>",
                                    actHoy, If(dtAyer IsNot Nothing, actAyer.ToString(), "-"), If(dt7Dias IsNot Nothing, act7D.ToString(), "-"), FormatearTendencia(actHoy, actAyer)))
        sb.AppendLine(String.Format("        <tr bgcolor=""#f8fafc"" style=""background-color: #f8fafc;""><td bgcolor=""#f8fafc"" style=""padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: #f8fafc;""><strong>Proyectos Atrasados / Críticos (ROJO)</strong></td><td bgcolor=""#f8fafc"" style=""text-align: center; font-weight: 700; color: #b91c1c; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; background-color: #f8fafc;"">{0}</td><td bgcolor=""#f8fafc"" style=""text-align: center; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: #f8fafc;"">{1}</td><td bgcolor=""#f8fafc"" style=""text-align: center; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: #f8fafc;"">{2}</td><td bgcolor=""#f8fafc"" style=""text-align: center; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; background-color: #f8fafc;"">{3}</td></tr>",
                                    atrasHoy, If(dtAyer IsNot Nothing, atrasAyer.ToString(), "-"), If(dt7Dias IsNot Nothing, atras7D.ToString(), "-"), FormatearTendenciaInversa(atrasHoy, atrasAyer)))
        sb.AppendLine(String.Format("        <tr bgcolor=""#ffffff"" style=""background-color: #ffffff;""><td bgcolor=""#ffffff"" style=""padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: #ffffff;""><strong>Proyectos Sin Movimiento (&#8805; 4 días)</strong></td><td bgcolor=""#ffffff"" style=""text-align: center; font-weight: 700; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: #ffffff;"">{0}</td><td bgcolor=""#ffffff"" style=""text-align: center; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: #ffffff;"">{1}</td><td bgcolor=""#ffffff"" style=""text-align: center; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: #ffffff;"">{2}</td><td bgcolor=""#ffffff"" style=""text-align: center; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; background-color: #ffffff;"">{3}</td></tr>",
                                    sinMovHoy, If(dtAyer IsNot Nothing, sinMovAyer.ToString(), "-"), If(dt7Dias IsNot Nothing, sinMov7D.ToString(), "-"), FormatearTendenciaInversa(sinMovHoy, sinMovAyer)))
        sb.AppendLine(String.Format("        <tr bgcolor=""#f8fafc"" style=""background-color: #f8fafc;""><td bgcolor=""#f8fafc"" style=""padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: #f8fafc;""><strong>Evolución Semáforo VERDE</strong></td><td bgcolor=""#f8fafc"" style=""text-align: center; font-weight: 700; color: #15803d; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; background-color: #f8fafc;"">{0}</td><td bgcolor=""#f8fafc"" style=""text-align: center; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: #f8fafc;"">{1}</td><td bgcolor=""#f8fafc"" style=""text-align: center; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: #f8fafc;"">{2}</td><td bgcolor=""#f8fafc"" style=""text-align: center; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; background-color: #f8fafc;"">{3}</td></tr>",
                                    vHoy, If(dtAyer IsNot Nothing, vAyer.ToString(), "-"), If(dt7Dias IsNot Nothing, v7D.ToString(), "-"), FormatearTendencia(vHoy, vAyer)))
        sb.AppendLine(String.Format("        <tr bgcolor=""#ffffff"" style=""background-color: #ffffff;""><td bgcolor=""#ffffff"" style=""padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: #ffffff;""><strong>Evolución Semáforo AMARILLO</strong></td><td bgcolor=""#ffffff"" style=""text-align: center; font-weight: 700; color: #a16207; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; background-color: #ffffff;"">{0}</td><td bgcolor=""#ffffff"" style=""text-align: center; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: #ffffff;"">{1}</td><td bgcolor=""#ffffff"" style=""text-align: center; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: #ffffff;"">{2}</td><td bgcolor=""#ffffff"" style=""text-align: center; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; background-color: #ffffff;"">{3}</td></tr>",
                                    aHoy, If(dtAyer IsNot Nothing, aAyer.ToString(), "-"), If(dt7Dias IsNot Nothing, a7D.ToString(), "-"), "-"))
        sb.AppendLine(String.Format("        <tr bgcolor=""#f8fafc"" style=""background-color: #f8fafc;""><td bgcolor=""#f8fafc"" style=""padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: #f8fafc;""><strong>Evolución Semáforo ROJO</strong></td><td bgcolor=""#f8fafc"" style=""text-align: center; font-weight: 700; color: #b91c1c; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; background-color: #f8fafc;"">{0}</td><td bgcolor=""#f8fafc"" style=""text-align: center; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: #f8fafc;"">{1}</td><td bgcolor=""#f8fafc"" style=""text-align: center; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: #f8fafc;"">{2}</td><td bgcolor=""#f8fafc"" style=""text-align: center; padding: 8px 10px; border-bottom: 1px solid #e2e8f0; background-color: #f8fafc;"">{3}</td></tr>",
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
    ''' Sección 7: Detalle Estructurado de Proyectos agrupados por Cliente y ordenados por Vendedor y Clasificación.
    ''' </summary>
    Private Function GenerarDetalleProyectosHtml(ByVal proyectos As List(Of ItemProyectoInforme)) As String
        Dim sb As New System.Text.StringBuilder()

        sb.AppendLine("    <div class=""sec-heading"" style=""font-size: 15px; font-weight: 700; color: #1e3a8a; margin: 20px 0 12px 0; padding-bottom: 6px; border-bottom: 2px solid #e2e8f0;"">&#128221; 7. Detalle Estructurado por Cliente</div>")

        Dim proyectosParaDetalle = proyectos.Where(Function(p) Not p.EsCanceladoODeclinado).ToList()
        Dim clientes = proyectosParaDetalle.GroupBy(Function(p) p.ClienteNombre).OrderBy(Function(g) g.Key)

        For Each grpCli In clientes
            Dim totalPryCli As Integer = grpCli.Count
            Dim mtoUSDCli As Double = grpCli.Where(Function(p) p.MonedaSiglas.Equals("USD", StringComparison.OrdinalIgnoreCase)).Sum(Function(p) p.TotalMonto)
            Dim mtoMXNCli As Double = grpCli.Where(Function(p) Not p.MonedaSiglas.Equals("USD", StringComparison.OrdinalIgnoreCase)).Sum(Function(p) p.TotalMonto)

            sb.AppendLine("    <div class=""clasif-block"" style=""margin-bottom: 20px; border: 1px solid #cbd5e1; border-radius: 6px; overflow: hidden; background-color: #ffffff;"">")
            sb.AppendLine(String.Format("      <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""#eff6ff"" class=""clasif-bar"" style=""width: 100%; border-collapse: collapse; background-color: #eff6ff; border-left: 5px solid #2563eb; border-bottom: 1px solid #dbeafe;"">" & _
                                        "<tr><td bgcolor=""#eff6ff"" style=""padding: 8px 12px; font-weight: 700; font-size: 13px; color: #1e40af; text-transform: uppercase; letter-spacing: 0.3px; background-color: #eff6ff;"">&#9658; Cliente: {0} &nbsp;<span style=""font-size: 11px; font-weight: normal; color: #64748b;"">({1} proyecto(s) &bull; ${2:N0} USD &bull; ${3:N0} MXN)</span></td></tr></table>",
                                        System.Net.WebUtility.HtmlEncode(grpCli.Key), totalPryCli, mtoUSDCli, mtoMXNCli))

            sb.AppendLine("      <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""#ffffff"" class=""data-table"" style=""width: 100%; border-collapse: collapse; font-size: 11px; margin-bottom: 0; background-color: #ffffff;"">")
            sb.AppendLine("        <thead>")
            sb.AppendLine("          <tr bgcolor=""#f1f5f9"">")
            sb.AppendLine("            <th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 7px 6px; border-bottom: 2px solid #cbd5e1; font-size: 10px; width: 9%; text-align: left;"">Folio</th>")
            sb.AppendLine("            <th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 7px 6px; border-bottom: 2px solid #cbd5e1; font-size: 10px; width: 14%; text-align: left;"">Vendedor</th>")
            sb.AppendLine("            <th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 7px 6px; border-bottom: 2px solid #cbd5e1; font-size: 10px; width: 12%; text-align: left;"">Clasificación</th>")
            sb.AppendLine("            <th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 7px 6px; border-bottom: 2px solid #cbd5e1; font-size: 10px; width: 17%; text-align: left;"">Descripción</th>")
            sb.AppendLine("            <th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 7px 6px; border-bottom: 2px solid #cbd5e1; font-size: 10px; width: 11%; text-align: left;"">Estatus</th>")
            sb.AppendLine("            <th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 7px 6px; border-bottom: 2px solid #cbd5e1; font-size: 10px; width: 7%; text-align: center;"">Últ. Mov.</th>")
            sb.AppendLine("            <th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 7px 6px; border-bottom: 2px solid #cbd5e1; font-size: 10px; width: 4%; text-align: center;"">Inact.</th>")
            sb.AppendLine("            <th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 7px 6px; border-bottom: 2px solid #cbd5e1; font-size: 10px; width: 9%; text-align: right;"">Monto</th>")
            sb.AppendLine("            <th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 7px 6px; border-bottom: 2px solid #cbd5e1; font-size: 10px; width: 11%; text-align: left;"">Próxima Acción / Resp.</th>")
            sb.AppendLine("            <th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 7px 6px; border-bottom: 2px solid #cbd5e1; font-size: 10px; width: 3%; text-align: center;"">Compromiso</th>")
            sb.AppendLine("            <th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 7px 6px; border-bottom: 2px solid #cbd5e1; font-size: 10px; width: 3%; text-align: center;"">Semáforo</th>")
            sb.AppendLine("          </tr>")
            sb.AppendLine("        </thead>")
            sb.AppendLine("        <tbody>")

            ' Ordenar proyectos dentro del cliente: primero por Vendedor, luego por Clasificación, luego por Folio
            Dim proyectosOrdenados = grpCli.OrderBy(Function(p) p.VendedorNombre) _
                                           .ThenBy(Function(p) p.ClasificacionNombre) _
                                           .ThenBy(Function(p) p.ProyectoId) _
                                           .ToList()

            Dim rowDetIdx As Integer = 0
            For Each p In proyectosOrdenados
                Dim badgeCls As String = If(p.Semaforo = "ROJO", "badge-r", If(p.Semaforo = "AMARILLO", "badge-a", "badge-v"))
                Dim badgeStyle As String = If(p.Semaforo = "ROJO",
                    "display: inline-block; background-color: #fee2e2; color: #b91c1c !important; border: 1px solid #fca5a5; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px;",
                    If(p.Semaforo = "AMARILLO",
                        "display: inline-block; background-color: #fef9c3; color: #a16207 !important; border: 1px solid #fde047; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px;",
                        "display: inline-block; background-color: #dcfce7; color: #15803d !important; border: 1px solid #86efac; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px;"))
                Dim fchCompStr As String = "-"
                If p.FechaCompromiso.HasValue Then
                    fchCompStr = String.Format("<span title=""{0}"">{1:dd/MM/yy}</span>", p.TipoFechaCompromiso, p.FechaCompromiso.Value)
                    If p.DiasParaCompromiso.HasValue Then
                        Dim colorDiasDet As String = If(p.DiasParaCompromiso.Value <= 10, "#b91c1c", If(p.DiasParaCompromiso.Value <= 20, "#d97706", "#15803d"))
                        Dim pesoDiasDet As String = If(p.DiasParaCompromiso.Value <= 20, "bold", "normal")
                        fchCompStr &= String.Format("<br/><span style=""color: {0}; font-size: 10px; font-weight: {1};"">({2}d)</span>", colorDiasDet, pesoDiasDet, p.DiasParaCompromiso.Value)
                    End If
                End If

                Dim descStr As String = System.Net.WebUtility.HtmlEncode(p.Titulo)
                If Not String.IsNullOrWhiteSpace(p.ClienteFinal) AndAlso Not p.ClienteFinal.Equals(p.ClienteNombre, StringComparison.OrdinalIgnoreCase) Then
                    descStr &= String.Format("<br/><span style=""font-size: 10px; color: #64748b;"">Final: {0}</span>", System.Net.WebUtility.HtmlEncode(p.ClienteFinal))
                End If

                Dim rowBg As String = If(rowDetIdx Mod 2 = 0, "#ffffff", "#f8fafc")
                sb.AppendLine(String.Format("          <tr bgcolor=""{0}"" style=""background-color: {0};"">", rowBg))
                sb.AppendLine(String.Format("            <td bgcolor=""{0}"" style=""padding: 6px 5px; border-bottom: 1px solid #e2e8f0; background-color: {0};""><strong style=""font-family: Consolas, monospace; color: #0f172a;"">{1}</strong></td>", rowBg, p.ProyectoId))
                sb.AppendLine(String.Format("            <td bgcolor=""{0}"" style=""padding: 6px 5px; border-bottom: 1px solid #e2e8f0; font-weight: 600; font-size: 11px; color: #1e293b; background-color: {0};"">{1}</td>", rowBg, System.Net.WebUtility.HtmlEncode(p.VendedorNombre)))
                sb.AppendLine(String.Format("            <td bgcolor=""{0}"" style=""padding: 6px 5px; border-bottom: 1px solid #e2e8f0; background-color: {0};""><span style=""font-size: 11px; color: #1e3a8a; font-weight: 600;"">{1}</span></td>", rowBg, System.Net.WebUtility.HtmlEncode(p.ClasificacionNombre)))
                sb.AppendLine(String.Format("            <td bgcolor=""{0}"" style=""padding: 6px 5px; border-bottom: 1px solid #e2e8f0; font-size: 11px; color: #1e293b; background-color: {0};"">{1}</td>", rowBg, descStr))
                sb.AppendLine(String.Format("            <td bgcolor=""{0}"" style=""padding: 6px 5px; border-bottom: 1px solid #e2e8f0; background-color: {0};""><span style=""font-size: 10px; background: #e2e8f0; padding: 2px 4px; border-radius: 3px; color: #1e293b;"">{1}</span></td>", rowBg, System.Net.WebUtility.HtmlEncode(p.EstatusNombre)))
                sb.AppendLine(String.Format("            <td bgcolor=""{0}"" style=""text-align: center; font-size: 11px; padding: 6px 5px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: {0};"">{1:dd/MM/yy}</td>", rowBg, p.FechaUltimoMovimiento))
                sb.AppendLine(String.Format("            <td bgcolor=""{0}"" style=""text-align: center; font-weight: {1}; color: {2}; padding: 6px 5px; border-bottom: 1px solid #e2e8f0; background-color: {0};"">{3}d</td>",
                                            rowBg,
                                            If(p.DiasSinMovimiento >= 4, "bold", "normal"),
                                            If(p.DiasSinMovimiento > 15, "#b91c1c", If(p.DiasSinMovimiento >= 4, "#d97706", "#16a34a")),
                                            p.DiasSinMovimiento))
                sb.AppendLine(String.Format("            <td bgcolor=""{0}"" style=""text-align: right; font-weight: bold; padding: 6px 5px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: {0};"">{1:C2} <span style=""font-size: 10px; color: #64748b;"">{2}</span></td>", rowBg, p.TotalMonto, p.MonedaSiglas))
                sb.AppendLine(String.Format("            <td bgcolor=""{0}"" style=""font-size: 10px; line-height: 1.3; padding: 6px 5px; border-bottom: 1px solid #e2e8f0; color: #334155; background-color: {0};"">{1}<br/><strong style=""color: #1e3a8a;"">{2}</strong></td>",
                                            rowBg, System.Net.WebUtility.HtmlEncode(p.ProximaAccion), System.Net.WebUtility.HtmlEncode(p.Responsable)))
                sb.AppendLine(String.Format("            <td bgcolor=""{0}"" style=""text-align: center; font-size: 10px; padding: 6px 5px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: {0};"">{1}</td>", rowBg, fchCompStr))
                sb.AppendLine(String.Format("            <td bgcolor=""{0}"" style=""text-align: center; padding: 6px 5px; border-bottom: 1px solid #e2e8f0; background-color: {0};""><span class=""{1}"" style=""{2}"">{3}</span></td>", rowBg, badgeCls, badgeStyle, p.Semaforo))
                sb.AppendLine("          </tr>")
                rowDetIdx += 1
            Next

            sb.AppendLine("        </tbody>")
            sb.AppendLine("      </table>")
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

        sb.AppendLine("    <div class=""sec-heading"" style=""font-size: 15px; font-weight: 700; color: #991b1b; margin: 20px 0 12px 0; padding-bottom: 6px; border-bottom: 2px solid #fecaca;"">&#128683; 8. Apartado Exclusivo: Proyectos Declinados (activo = 'CERRADO')</div>")
        sb.AppendLine("    <p style=""font-size: 12px; color: #64748b; margin: -6px 0 16px 0;"">Relación de proyectos marcados en sistema con estatus <strong>CERRADO / DECLINADO</strong> fuera del embudo comercial activo, detallando la justificación y seguimiento registrado en bitácora.</p>")

        If declinados.Count = 0 Then
            sb.AppendLine("    <div style=""padding: 14px 18px; background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 6px; font-size: 12px; color: #64748b; margin-bottom: 20px;"">")
            sb.AppendLine("      &#10003; <strong>Sin proyectos declinados:</strong> No se identificaron proyectos con estatus cerrado o declinado en el periodo evaluado.")
            sb.AppendLine("    </div>")
            Return sb.ToString()
        End If

        For Each p In declinados
            sb.AppendLine("    <div style=""margin-bottom: 20px; border: 1px solid #fecaca; border-radius: 6px; overflow: hidden; background-color: #ffffff;"">")

            ' Encabezado claro del proyecto declinado
            sb.AppendLine("      <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""#fef2f2"" style=""width: 100%; border-collapse: collapse; background-color: #fef2f2; border-left: 5px solid #dc2626; border-bottom: 1px solid #fecaca;"">")
            sb.AppendLine("        <tr>")
            sb.AppendLine(String.Format("          <td bgcolor=""#fef2f2"" style=""padding: 9px 14px; font-size: 13px; font-weight: 700; color: #991b1b; background-color: #fef2f2;""><span style=""background-color: #dc2626; color: #ffffff !important; padding: 2px 7px; border-radius: 4px; font-size: 10px; font-weight: 800; letter-spacing: 0.5px; text-transform: uppercase; margin-right: 8px;"">DECLINADO</span> Folio: <span style=""font-family: Consolas, monospace; font-size: 13px; color: #0f172a;"">{0}</span> &bull; <span style=""color: #1e293b;"">{1}</span></td>",
                                        p.ProyectoId, System.Net.WebUtility.HtmlEncode(p.ClienteNombre)))
            sb.AppendLine(String.Format("          <td bgcolor=""#fef2f2"" style=""text-align: right; color: #991b1b; font-weight: 800; font-size: 13px; padding: 9px 14px; background-color: #fef2f2;"">{0:C2} {1}</td>", p.TotalMonto, p.MonedaSiglas))
            sb.AppendLine("        </tr>")
            sb.AppendLine("      </table>")

            ' Ficha técnica y comercial
            sb.AppendLine("      <div style=""padding: 10px 14px; background-color: #f8fafc; border-bottom: 1px solid #e2e8f0; font-size: 12px; color: #334155;"">")
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
            sb.AppendLine("      <div style=""padding: 12px 14px; background-color: #ffffff;"">")
            sb.AppendLine("        <div style=""font-size: 12px; font-weight: 700; color: #0f172a; margin-bottom: 8px;"">&#128220; Trazabilidad y Seguimiento Registrado en Bitácora:</div>")

            If p.Seguimientos IsNot Nothing AndAlso p.Seguimientos.Count > 0 Then
                sb.AppendLine("        <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""#ffffff"" class=""data-table"" style=""width: 100%; border-collapse: collapse; margin-bottom: 0; background-color: #ffffff;"">")
                sb.AppendLine("          <thead>")
                sb.AppendLine("            <tr bgcolor=""#f1f5f9"">")
                sb.AppendLine("              <th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 6px 8px; border-bottom: 2px solid #cbd5e1; font-size: 10px; width: 5%; text-align: center;"">#</th>")
                sb.AppendLine("              <th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 6px 8px; border-bottom: 2px solid #cbd5e1; font-size: 10px; width: 16%; text-align: center;"">Fecha / Hora</th>")
                sb.AppendLine("              <th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 6px 8px; border-bottom: 2px solid #cbd5e1; font-size: 10px; width: 24%; text-align: left;"">Usuario Registrador</th>")
                sb.AppendLine("              <th bgcolor=""#f1f5f9"" style=""background-color: #f1f5f9; color: #334155; font-weight: 700; padding: 6px 8px; border-bottom: 2px solid #cbd5e1; font-size: 10px; width: 55%; text-align: left;"">Detalle del Seguimiento Registrado</th>")
                sb.AppendLine("            </tr>")
                sb.AppendLine("          </thead>")
                sb.AppendLine("          <tbody>")

                Dim idx As Integer = 1
                For Each seg In p.Seguimientos
                    Dim esNotaResolucion As Boolean = seg.Detalle.IndexOf("declina", StringComparison.OrdinalIgnoreCase) >= 0 OrElse _
                                                     seg.Detalle.IndexOf("cancela", StringComparison.OrdinalIgnoreCase) >= 0 OrElse _
                                                     seg.Detalle.IndexOf("cerrad", StringComparison.OrdinalIgnoreCase) >= 0

                    Dim rowBg As String = If(esNotaResolucion, "#fef2f2", If(idx Mod 2 = 0, "#ffffff", "#f8fafc"))
                    Dim textStyle As String = If(esNotaResolucion, "color: #991b1b; font-weight: 600;", "color: #334155;")
                    Dim badgeResolucion As String = If(esNotaResolucion, "<span style=""display: inline-block; background-color: #dc2626; color: #ffffff !important; font-size: 9px; font-weight: 700; padding: 1px 5px; border-radius: 3px; margin-right: 4px;"">RESOLUCIÓN</span> ", "")

                    sb.AppendLine(String.Format("            <tr bgcolor=""{0}"" style=""background-color: {0};"">", rowBg))
                    sb.AppendLine(String.Format("              <td bgcolor=""{0}"" style=""text-align: center; font-size: 11px; color: #64748b; padding: 6px 8px; border-bottom: 1px solid #e2e8f0; background-color: {0};"">{1}</td>", rowBg, idx))
                    sb.AppendLine(String.Format("              <td bgcolor=""{0}"" style=""text-align: center; font-size: 11px; color: #1e293b; padding: 6px 8px; border-bottom: 1px solid #e2e8f0; background-color: {0};"">{1:dd/MM/yyyy HH:mm}</td>", rowBg, seg.Fecha))
                    sb.AppendLine(String.Format("              <td bgcolor=""{0}"" style=""font-size: 11px; padding: 6px 8px; border-bottom: 1px solid #e2e8f0; color: #1e293b; background-color: {0};""><strong>{1}</strong><br/><span style=""font-size: 10px; color: #64748b;"">{2}</span></td>",
                                                rowBg, System.Net.WebUtility.HtmlEncode(seg.UsuarioNombre), System.Net.WebUtility.HtmlEncode(seg.UsuarioClave)))
                    sb.AppendLine(String.Format("              <td bgcolor=""{0}"" style=""font-size: 11px; line-height: 1.4; padding: 6px 8px; border-bottom: 1px solid #e2e8f0; background-color: {0}; {1}"">{2}{3}</td>",
                                                rowBg, textStyle, badgeResolucion, System.Net.WebUtility.HtmlEncode(seg.Detalle).Replace(vbCrLf, "<br/>").Replace(vbLf, "<br/>")))
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

#Region "Notificaciones Diarias de Seguimiento de Cotizaciones a Vendedores"

    ''' <summary>
    ''' Representa un elemento de cotización pendiente asignado a un vendedor (Cliente o Interna).
    ''' </summary>
    Public Class ItemCotizacionSeguimientoVendedor
        Public Property VentaId As Integer
        Public Property ProyectoId As String
        Public Property Titulo As String
        Public Property ClienteNombre As String
        Public Property FolioCotizacion As String
        Public Property FechaBase As DateTime
        Public Property FechaVigencia As Nullable(Of DateTime)
        Public Property Dias As Integer
        Public Property Semaforo As String
        Public Property TotalMonto As Double
        Public Property MonedaSiglas As String
        Public Property QueEstaPendiente As String
        Public Property Responsable As String
        Public Property ProximaAccion As String
        Public Property BadgeDiasTexto As String
        Public Property EsCotizacionInterna As Boolean
    End Class

    ''' <summary>
    ''' Agrupa los datos del vendedor y sus cotizaciones clasificadas por sección.
    ''' </summary>
    Public Class ResumenVendedorSeguimiento
        Public Property VendedorClave As String
        Public Property VendedorNombre As String
        Public Property Email As String
        Public Property CotizacionesCliente As New List(Of ItemCotizacionSeguimientoVendedor)()
        Public Property CotizacionesInternas As New List(Of ItemCotizacionSeguimientoVendedor)()

        Public ReadOnly Property TotalPendientes As Integer
            Get
                Return CotizacionesCliente.Count + CotizacionesInternas.Count
            End Get
        End Property
    End Class

    ''' <summary>
    ''' Rutina matutina programada para enviarse de lunes a viernes a las 8:15 AM
    ''' a cada vendedor con el seguimiento individualizado de:
    ''' 1) Cotizaciones de clientes pendientes de recibir Orden de Compra (Estatus 5).
    ''' 2) Cotizaciones internas pendientes de generar cotización a cliente (Estatus 4).
    ''' </summary>
    Public Sub NotificarSeguimientoCotizacionesVentas(Optional ByVal forzarEnvio As Boolean = False)
        If _procesandoSeguimientoVentas Then Return

        ' 1. Omitir sábados y domingos (salvo si es forzado manualmente para pruebas)
        If Not forzarEnvio Then
            If DateTime.Now.DayOfWeek = DayOfWeek.Saturday OrElse DateTime.Now.DayOfWeek = DayOfWeek.Sunday Then
                Return
            End If

            ' 2. Validar horario de envío diario (a partir de las 08:15 AM)
            Dim horaProgramada As New TimeSpan(8, 15, 0)
            If DateTime.Now.TimeOfDay < horaProgramada Then
                Return
            End If

            ' Validar que no se haya ejecutado ya el día de hoy
            If _fechaUltimoEnvioSeguimientoVentas.HasValue AndAlso _fechaUltimoEnvioSeguimientoVentas.Value.Date = DateTime.Now.Date Then
                Return
            End If
        End If

        _procesandoSeguimientoVentas = True
        Try
            If cx_MySQL_local.State <> ConnectionState.Open Then
                Try
                    If cx_MySQL_local.State = ConnectionState.Broken Then cx_MySQL_local.Close()
                    cx_MySQL_local.Open()
                Catch exConn As Exception
                    AgregarLog(500, "[Seguimiento Ventas] Error al conectar a la BD local: " & exConn.Message)
                    Return
                End Try
            End If

            ' Obtener correos de directivos para envío en copia (CC) desde cat_consultorio
            Dim correosDirectivos As String = ""
            Dim dtDirectivos As DataTable = tb_Recordset_MySQL_local("SELECT correos_solo_directivos FROM cat_consultorio LIMIT 1")
            If dtDirectivos IsNot Nothing AndAlso dtDirectivos.Rows.Count > 0 AndAlso Not IsDBNull(dtDirectivos.Rows(0)("correos_solo_directivos")) Then
                correosDirectivos = dtDirectivos.Rows(0)("correos_solo_directivos").ToString().Trim()
            End If

            ' 3. Obtener lista de vendedores agrupados con proyectos en seguimiento
            Dim sqlVendedores As String =
                "SELECT DISTINCT " & _
                "  v.ccveusuario_vendedor, " & _
                "  COALESCE(TRIM(CONCAT_WS(' ', m.cnombre, m.cpriapellido, m.csegapellido)), v.ccveusuario_vendedor, 'SIN ASIGNAR') AS vendedor_nombre, " & _
                "  COALESCE(m.email, '') AS email " & _
                "FROM tb_ventas v " & _
                "LEFT JOIN cat_medico m ON v.ccveusuario_vendedor = m.ccvemedico " & _
                "WHERE v.ccveusuario_vendedor IS NOT NULL " & _
                "  AND v.ccveusuario_vendedor <> '' " & _
                "  AND v.activo = 'ACTIVO' " & _
                "  AND v.fecha >= '2026-08-24' " & _
                "  AND v.estatus_proyecto_id IN (4, 5) " & _
                "ORDER BY vendedor_nombre;"

            Dim dtVendedores As DataTable = tb_Recordset_MySQL_local(sqlVendedores)
            If dtVendedores Is Nothing OrElse dtVendedores.Rows.Count = 0 Then
                LogEventos.Escribir("[Seguimiento Ventas] No se encontraron vendedores con cotizaciones pendientes (a partir de 2026-08-24).")
                Return
            End If

            Dim totalVendedoresNotificados As Integer = 0

            For Each rVend As DataRow In dtVendedores.Rows
                Dim cveVendedor As String = rVend("ccveusuario_vendedor").ToString().Trim()
                Dim nomVendedor As String = rVend("vendedor_nombre").ToString().Trim()
                Dim emailVendedor As String = rVend("email").ToString().Trim()

                ' Validar si hoy ya se le envió su reporte a este vendedor específico
                If Not forzarEnvio Then
                    Dim sqlCheckVend As String = String.Format(
                        "SELECT COUNT(*) FROM tb_seguimiento_ventas_envio_log WHERE fecha = CURDATE() AND ccveusuario_vendedor = '{0}' AND enviado = 1",
                        cveVendedor.Replace("'", "''"))
                    Dim dtCheck As DataTable = tb_Recordset_MySQL_local(sqlCheckVend)
                    If dtCheck IsNot Nothing AndAlso dtCheck.Rows.Count > 0 AndAlso Convert.ToInt32(dtCheck.Rows(0)(0)) > 0 Then
                        ' Ya enviado hoy a este vendedor, continuar con el siguiente
                        Continue For
                    End If
                End If

                ' Cargar datos y cotizaciones del vendedor
                Dim resumen As ResumenVendedorSeguimiento = ObtenerResumenCotizacionesVendedor(cveVendedor, nomVendedor, emailVendedor)

                ' Si no tiene cotizaciones pendientes en ninguna sección, omitir envío
                If resumen.TotalPendientes = 0 Then
                    Continue For
                End If

                ' Validar que cuente con correo válido en cat_medico
                If String.IsNullOrWhiteSpace(resumen.Email) OrElse Not EsDireccionCorreoValida(resumen.Email) Then
                    AgregarLog(500, String.Format("[Seguimiento Ventas] Vendedor {0} ({1}) no tiene correo válido en cat_medico: '{2}'.", nomVendedor, cveVendedor, resumen.Email))
                    RegistrarLogEnvioVentas(cveVendedor, nomVendedor, resumen.Email, resumen.CotizacionesCliente.Count, resumen.CotizacionesInternas.Count, False, "Correo inválido o no configurado en cat_medico")
                    Continue For
                End If

                ' Generar HTML del informe individualizado
                Dim htmlCuerpo As String = GenerarHTMLSeguimientoVendedor(resumen)
                Dim asunto As String = String.Format("Informe Diario de Seguimiento Comercial - {0} ({1:dd/MM/yyyy})", nomVendedor, DateTime.Now)

                ' Enviar correo HTML mediante Chilkat con copia a directivos si aplica
                Dim enviadoExitoso As Boolean = EnviarCorreoNotificacionHTML(resumen.Email, asunto, htmlCuerpo, correosDirectivos)
                If enviadoExitoso Then
                    totalVendedoresNotificados += 1
                    'AgregarLog(100, String.Format("[Seguimiento Ventas] Notificación enviada a {0} ({1}){2} - Cotiz. Cliente: {3}, Cotiz. Internas: {4}.",
                    '                              nomVendedor, resumen.Email, If(Not String.IsNullOrWhiteSpace(correosDirectivos), " [CC: " & correosDirectivos & "]", ""), resumen.CotizacionesCliente.Count, resumen.CotizacionesInternas.Count))
                    RegistrarLogEnvioVentas(cveVendedor, nomVendedor, resumen.Email, resumen.CotizacionesCliente.Count, resumen.CotizacionesInternas.Count, True, "Enviado con éxito")
                Else
                    AgregarLog(500, String.Format("[Seguimiento Ventas] Error al enviar correo a {0} ({1}).", nomVendedor, resumen.Email))
                    RegistrarLogEnvioVentas(cveVendedor, nomVendedor, resumen.Email, resumen.CotizacionesCliente.Count, resumen.CotizacionesInternas.Count, False, "Error al enviar mediante Chilkat")
                End If
            Next

            _fechaUltimoEnvioSeguimientoVentas = DateTime.Now
            LogEventos.Escribir(String.Format("[Seguimiento Ventas] Ciclo completado. Vendedores notificados hoy: {0}.", totalVendedoresNotificados))

        Catch ex As Exception
            AgregarLog(500, "Error en NotificarSeguimientoCotizacionesVentas: " & ex.Message)
            LogEventos.Escribir("Error en NotificarSeguimientoCotizacionesVentas: " & ex.Message & " - Stack: " & ex.StackTrace)
        Finally
            _procesandoSeguimientoVentas = False
        End Try
    End Sub

    ''' <summary>
    ''' Registra en tb_seguimiento_ventas_envio_log el resultado del envío diario a un vendedor.
    ''' </summary>
    Private Sub RegistrarLogEnvioVentas(ByVal cveVendedor As String, ByVal nomVendedor As String, ByVal email As String,
                                        ByVal totalCliente As Integer, ByVal totalInternas As Integer, ByVal enviado As Boolean, ByVal mensajeError As String)
        Try
            Dim sqlInsert As String = String.Format(
                "INSERT INTO tb_seguimiento_ventas_envio_log (fecha, ccveusuario_vendedor, vendedor_nombre, email, total_cotizaciones_cliente, total_cotizaciones_internas, enviado, fchregistro, mensaje_error) " & _
                "VALUES (CURDATE(), '{0}', '{1}', '{2}', {3}, {4}, {5}, NOW(), '{6}');",
                cveVendedor.Replace("'", "''"), nomVendedor.Replace("'", "''"), email.Replace("'", "''"),
                totalCliente, totalInternas, If(enviado, 1, 0), mensajeError.Replace("'", "''"))
            Using cmm As New MySqlConnector.MySqlCommand(sqlInsert, cx_MySQL_local)
                cmm.ExecuteNonQuery()
            End Using
        Catch ex As Exception
            LogEventos.Escribir("Error en RegistrarLogEnvioVentas: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Consulta y clasifica las cotizaciones pendientes de un vendedor en:
    ''' 1) Cotizaciones de clientes pendientes de orden de compra (Estatus 5).
    ''' 2) Cotizaciones internas pendientes de cotización a cliente (Estatus 4).
    ''' </summary>
    Private Function ObtenerResumenCotizacionesVendedor(ByVal cveVendedor As String, ByVal nomVendedor As String, ByVal email As String) As ResumenVendedorSeguimiento
        Dim resumen As New ResumenVendedorSeguimiento()
        resumen.VendedorClave = cveVendedor
        resumen.VendedorNombre = nomVendedor
        resumen.Email = email

        ' -------------------------------------------------------------
        ' Sección 1: Cotizaciones de Clientes Pendientes de Orden de Compra (Estatus 5)
        ' -------------------------------------------------------------
        Dim sqlSec1 As String = String.Format(
            "SELECT " & _
            "  v.id AS venta_id, " & _
            "  COALESCE(v.proyecto_id, '') AS proyecto_id, " & _
            "  COALESCE(v.titulo, '') AS titulo, " & _
            "  v.fecha AS fecha_proyecto, " & _
            "  COALESCE(cli.nombre_comercial, cli.razon_social, 'CLIENTE NO DEFINIDO') AS cliente_nombre, " & _
            "  COALESCE(cc.folio_cotizacion, '') AS folio_cotizacion, " & _
            "  COALESCE(cc.fecha, v.fecha) AS fecha_cotizacion, " & _
            "  cc.fecha_vigencia, " & _
            "  COALESCE(NULLIF(cc.total, 0), v.total, 0) AS total_monto, " & _
            "  COALESCE(tc.siglas, 'USD') AS moneda_siglas " & _
            "FROM tb_ventas v " & _
            "LEFT JOIN cat_clientes cli ON v.cliente_id = cli.id " & _
            "LEFT JOIN tb_ventas_cotizacion_cliente cc ON cc.id = ( " & _
            "    SELECT MAX(id) FROM tb_ventas_cotizacion_cliente " & _
            "    WHERE venta_id = v.id AND activo = 1 " & _
            ") " & _
            "LEFT JOIN cat_tipos_cambio tc ON COALESCE(cc.moneda_id, v.moneda_id) = tc.id " & _
            "WHERE v.ccveusuario_vendedor = '{0}' " & _
            "  AND v.estatus_proyecto_id = 5 " & _
            "  AND v.activo = 'ACTIVO' " & _
            "  AND v.fecha >= '2026-08-24' " & _
            "ORDER BY cc.fecha_vigencia ASC, total_monto DESC;", cveVendedor.Replace("'", "''"))

        Dim dtSec1 As DataTable = tb_Recordset_MySQL_local(sqlSec1)
        If dtSec1 IsNot Nothing Then
            For Each r As DataRow In dtSec1.Rows
                Dim item As New ItemCotizacionSeguimientoVendedor()
                item.EsCotizacionInterna = False
                item.VentaId = Convert.ToInt32(r("venta_id"))
                item.ProyectoId = r("proyecto_id").ToString().Trim()
                item.Titulo = r("titulo").ToString().Trim()
                item.ClienteNombre = r("cliente_nombre").ToString().Trim()
                item.FolioCotizacion = r("folio_cotizacion").ToString().Trim()
                item.TotalMonto = If(Not IsDBNull(r("total_monto")), Convert.ToDouble(r("total_monto")), 0)
                item.MonedaSiglas = If(Not IsDBNull(r("moneda_siglas")), r("moneda_siglas").ToString().Trim(), "USD")
                item.Responsable = "Ventas (" & nomVendedor & ")"
                item.ProximaAccion = "Seguimiento comercial con cliente para cierre de venta y recepción de OC"

                If Not IsDBNull(r("fecha_vigencia")) Then
                    Dim fVig As DateTime
                    If DateTime.TryParse(r("fecha_vigencia").ToString(), fVig) Then
                        item.FechaVigencia = fVig
                    End If
                End If

                If item.FechaVigencia.HasValue Then
                    Dim dRest As Integer = CInt(Math.Floor((item.FechaVigencia.Value.Date - DateTime.Now.Date).TotalDays))
                    item.Dias = dRest
                    If dRest < 0 Then
                        item.Semaforo = "ROJO"
                        item.BadgeDiasTexto = String.Format("&#9888; Vencido hace {0} día(s)", Math.Abs(dRest))
                        item.QueEstaPendiente = String.Format("Vigencia/Respuesta de cotización vencida hace {0} día(s) (Vigencia: {1:dd/MM/yy})", Math.Abs(dRest), item.FechaVigencia.Value)
                    ElseIf dRest = 0 Then
                        item.Semaforo = "ROJO"
                        item.BadgeDiasTexto = "&#9888; Vence HOY para respuesta"
                        item.QueEstaPendiente = String.Format("Vigencia/Respuesta de cotización vence HOY ({0:dd/MM/yy})", item.FechaVigencia.Value)
                    ElseIf dRest = 1 Then
                        item.Semaforo = "ROJO"
                        item.BadgeDiasTexto = "&#9203; Falta 1 día para respuesta"
                        item.QueEstaPendiente = String.Format("Falta 1 día para recibir respuesta del cliente (Vigencia: {0:dd/MM/yy})", item.FechaVigencia.Value)
                    ElseIf dRest <= 10 Then
                        item.Semaforo = "ROJO"
                        item.BadgeDiasTexto = String.Format("&#9203; Faltan {0} días para respuesta", dRest)
                        item.QueEstaPendiente = String.Format("Faltan {0} días para recibir respuesta del cliente (Vigencia: {1:dd/MM/yy})", dRest, item.FechaVigencia.Value)
                    ElseIf dRest <= 20 Then
                        item.Semaforo = "AMARILLO"
                        item.BadgeDiasTexto = String.Format("&#9203; Faltan {0} días para respuesta", dRest)
                        item.QueEstaPendiente = String.Format("Faltan {0} días para recibir respuesta del cliente (Vigencia: {1:dd/MM/yy})", dRest, item.FechaVigencia.Value)
                    Else
                        item.Semaforo = "VERDE"
                        item.BadgeDiasTexto = String.Format("&#9203; Faltan {0} días para respuesta", dRest)
                        item.QueEstaPendiente = String.Format("Cotización a cliente en tiempo (Faltan {0} días, Vigencia: {1:dd/MM/yy})", dRest, item.FechaVigencia.Value)
                    End If
                Else
                    item.Semaforo = "VERDE"
                    item.BadgeDiasTexto = "En plazo"
                    item.QueEstaPendiente = "Cotización elaborada al cliente en tiempo"
                End If

                resumen.CotizacionesCliente.Add(item)
            Next
        End If

        ' -------------------------------------------------------------
        ' Sección 2: Cotizaciones Internas Pendientes de Cotizar a Cliente (Estatus 4)
        ' -------------------------------------------------------------
        Dim sqlSec2 As String = String.Format(
            "SELECT " & _
            "  v.id AS venta_id, " & _
            "  COALESCE(v.proyecto_id, '') AS proyecto_id, " & _
            "  COALESCE(v.titulo, '') AS titulo, " & _
            "  v.fecha AS fecha_proyecto, " & _
            "  COALESCE(cli.nombre_comercial, cli.razon_social, 'CLIENTE NO DEFINIDO') AS cliente_nombre, " & _
            "  COALESCE(ci.folio_cotizacion, '') AS folio_cotizacion, " & _
            "  COALESCE(ci.fecha, v.fecha) AS fecha_cotizacion, " & _
            "  COALESCE(ci.fchregistro, v.fchregistroactualiza, v.fchregistro) AS fecha_base_interna, " & _
            "  COALESCE(NULLIF(ci.total, 0), v.total, 0) AS total_monto, " & _
            "  COALESCE(tc.siglas, 'USD') AS moneda_siglas " & _
            "FROM tb_ventas v " & _
            "LEFT JOIN cat_clientes cli ON v.cliente_id = cli.id " & _
            "LEFT JOIN tb_compras_cotizacion_interna ci ON ci.id = ( " & _
            "    SELECT MAX(id) FROM tb_compras_cotizacion_interna " & _
            "    WHERE venta_id = v.id " & _
            ") " & _
            "LEFT JOIN cat_tipos_cambio tc ON COALESCE(ci.moneda_id, v.moneda_id) = tc.id " & _
            "WHERE v.ccveusuario_vendedor = '{0}' " & _
            "  AND v.estatus_proyecto_id = 4 " & _
            "  AND v.activo = 'ACTIVO' " & _
            "  AND v.fecha >= '2026-08-24' " & _
            "ORDER BY fecha_base_interna ASC, total_monto DESC;", cveVendedor.Replace("'", "''"))

        Dim dtSec2 As DataTable = tb_Recordset_MySQL_local(sqlSec2)
        If dtSec2 IsNot Nothing Then
            For Each r As DataRow In dtSec2.Rows
                Dim item As New ItemCotizacionSeguimientoVendedor()
                item.EsCotizacionInterna = True
                item.VentaId = Convert.ToInt32(r("venta_id"))
                item.ProyectoId = r("proyecto_id").ToString().Trim()
                item.Titulo = r("titulo").ToString().Trim()
                item.ClienteNombre = r("cliente_nombre").ToString().Trim()
                item.FolioCotizacion = r("folio_cotizacion").ToString().Trim()
                item.TotalMonto = If(Not IsDBNull(r("total_monto")), Convert.ToDouble(r("total_monto")), 0)
                item.MonedaSiglas = If(Not IsDBNull(r("moneda_siglas")), r("moneda_siglas").ToString().Trim(), "USD")
                item.Responsable = "Ventas (" & nomVendedor & ")"
                item.ProximaAccion = "Generar y enviar formalmente la cotización de venta al cliente a la brevedad"

                Dim fBase As DateTime = DateTime.Now
                If Not IsDBNull(r("fecha_base_interna")) AndAlso DateTime.TryParse(r("fecha_base_interna").ToString(), fBase) Then
                    item.FechaBase = fBase
                Else
                    item.FechaBase = DateTime.Now
                End If

                Dim dTrans As Integer = CInt(Math.Floor((DateTime.Now.Date - item.FechaBase.Date).TotalDays))
                If dTrans < 0 Then dTrans = 0
                item.Dias = dTrans

                If dTrans <= 1 Then
                    item.Semaforo = "VERDE"
                    item.BadgeDiasTexto = If(dTrans = 0, "&#9203; Elaborada HOY", "&#9203; Elaborada hace 1 día")
                    item.QueEstaPendiente = String.Format("Cotización interna lista desde el {0:dd/MM/yyyy}. Elaborada recientemente, pendiente generar cotización a cliente.", item.FechaBase)
                ElseIf dTrans <= 3 Then
                    item.Semaforo = "AMARILLO"
                    item.BadgeDiasTexto = String.Format("&#9203; Han transcurrido {0} días sin cotizar a cliente", dTrans)
                    item.QueEstaPendiente = String.Format("Cotización interna lista desde el {0:dd/MM/yyyy}. Han transcurrido {1} días sin generar la cotización formal al cliente.", item.FechaBase, dTrans)
                Else
                    item.Semaforo = "ROJO"
                    item.BadgeDiasTexto = String.Format("&#9888; Han transcurrido {0} días sin cotizar a cliente", dTrans)
                    item.QueEstaPendiente = String.Format("Cotización interna lista desde el {0:dd/MM/yyyy}. Han transcurrido {1} días sin generar la cotización formal al cliente (Atención urgente).", item.FechaBase, dTrans)
                End If

                resumen.CotizacionesInternas.Add(item)
            Next
        End If

        Return resumen
    End Function

    ''' <summary>
    ''' Genera la tarjeta HTML individual para una cotización (Sección 1 o 2), con diseño idéntico a la imagen provista.
    ''' </summary>
    Private Function GenerarTarjetaSeguimientoHtml(ByVal item As ItemCotizacionSeguimientoVendedor) As String
        Dim badgeClass As String = If(item.Semaforo = "ROJO", "badge-r", If(item.Semaforo = "AMARILLO", "badge-a", "badge-v"))
        Dim badgeStyle As String = If(item.Semaforo = "ROJO",
            "display: inline-block; background-color: #fee2e2; color: #b91c1c !important; border: 1px solid #fca5a5; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px;",
            If(item.Semaforo = "AMARILLO",
                "display: inline-block; background-color: #fef9c3; color: #a16207 !important; border: 1px solid #fde047; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px;",
                "display: inline-block; background-color: #dcfce7; color: #15803d !important; border: 1px solid #86efac; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px;"))
        Dim borderCol As String = If(item.Semaforo = "ROJO", "#dc2626", If(item.Semaforo = "AMARILLO", "#d97706", "#16a34a"))
        Dim bgCol As String = If(item.Semaforo = "ROJO", "#fff1f2", If(item.Semaforo = "AMARILLO", "#fffbeb", "#f0fdf4"))

        Dim bgDias As String = If(item.Semaforo = "ROJO", "#fee2e2", If(item.Semaforo = "AMARILLO", "#fef9c3", "#dcfce7"))
        Dim colDias As String = If(item.Semaforo = "ROJO", "#b91c1c", If(item.Semaforo = "AMARILLO", "#a16207", "#15803d"))
        Dim borderDias As String = If(item.Semaforo = "ROJO", "#fca5a5", If(item.Semaforo = "AMARILLO", "#fde047", "#86efac"))

        Dim sb As New System.Text.StringBuilder()
        sb.AppendLine(String.Format("    <div class=""card-prio"" style=""border-left: 5px solid {0}; background-color: {1}; padding: 12px 16px; margin-bottom: 12px; border-radius: 4px; border-top: 1px solid #e2e8f0; border-right: 1px solid #e2e8f0; border-bottom: 1px solid #e2e8f0;"">", borderCol, bgCol))
        sb.AppendLine(String.Format("      <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""{0}"" style=""width: 100%; border-collapse: collapse; background-color: {0};"">", bgCol))
        sb.AppendLine("        <tr>")
        sb.AppendLine(String.Format("          <td style=""font-size: 13px; font-weight: 700; color: #0f172a;""><span class=""{0}"" style=""{1}"">&#9679; {2}</span> &nbsp; Folio: <span style=""font-family: Consolas, monospace;"">{3}</span> &bull; {4} &nbsp;<span style=""display: inline-block; background-color: {5}; color: {6} !important; border: 1px solid {7}; padding: 2px 8px; border-radius: 10px; font-weight: 700; font-size: 11px; vertical-align: middle;"">{8}</span></td>",
                                    badgeClass, badgeStyle, item.Semaforo, item.ProyectoId, System.Net.WebUtility.HtmlEncode(item.ClienteNombre), bgDias, colDias, borderDias, item.BadgeDiasTexto))
        sb.AppendLine(String.Format("          <td style=""text-align: right; font-weight: 800; font-size: 13px; color: #0f172a; white-space: nowrap;"">{0:C2} {1}</td>", item.TotalMonto, item.MonedaSiglas))
        sb.AppendLine("        </tr>")
        sb.AppendLine("      </table>")
        sb.AppendLine(String.Format("      <div style=""font-size: 12px; color: #334155; margin: 5px 0;""><strong>Descripción:</strong> {0}</div>", System.Net.WebUtility.HtmlEncode(item.Titulo)))
        sb.AppendLine("      <div class=""card-prio-meta"" style=""font-size: 11px; color: #475569; line-height: 1.5;"">")
        sb.AppendLine(String.Format("        &bull; <strong>Qué está pendiente:</strong> <span style=""color: {0}; font-weight: 600;"">{1}</span><br/>", If(item.Semaforo = "ROJO", "#991b1b", If(item.Semaforo = "AMARILLO", "#a16207", "#15803d")), System.Net.WebUtility.HtmlEncode(item.QueEstaPendiente)))
        sb.AppendLine(String.Format("        &bull; <strong>Quién es responsable:</strong> <span style=""color: #0f172a; font-weight: 600;"">{0}</span><br/>", System.Net.WebUtility.HtmlEncode(item.Responsable)))
        sb.AppendLine(String.Format("        &bull; <strong>Acción recomendada:</strong> <span style=""color: #1e3a8a; font-weight: 600;"">{0}</span>", System.Net.WebUtility.HtmlEncode(item.ProximaAccion)))
        sb.AppendLine("      </div>")
        sb.AppendLine("    </div>")

        Return sb.ToString()
    End Function

    ''' <summary>
    ''' Genera el cuerpo completo HTML del informe individualizado para el vendedor.
    ''' </summary>
    Private Function GenerarHTMLSeguimientoVendedor(ByVal resumen As ResumenVendedorSeguimiento) As String
        Dim sb As New System.Text.StringBuilder()

        Dim totalCliente As Integer = resumen.CotizacionesCliente.Count
        Dim totalInternas As Integer = resumen.CotizacionesInternas.Count

        Dim todasLasCotizaciones = resumen.CotizacionesCliente.Concat(resumen.CotizacionesInternas).ToList()
        Dim rojosCount As Integer = todasLasCotizaciones.Where(Function(x) x.Semaforo = "ROJO").Count()
        Dim amarillosCount As Integer = todasLasCotizaciones.Where(Function(x) x.Semaforo = "AMARILLO").Count()
        Dim verdesCount As Integer = todasLasCotizaciones.Where(Function(x) x.Semaforo = "VERDE").Count()

        Dim totalUSD As Double = todasLasCotizaciones.Where(Function(x) x.MonedaSiglas.Equals("USD", StringComparison.OrdinalIgnoreCase)).Sum(Function(x) x.TotalMonto)
        Dim totalMXN As Double = todasLasCotizaciones.Where(Function(x) Not x.MonedaSiglas.Equals("USD", StringComparison.OrdinalIgnoreCase)).Sum(Function(x) x.TotalMonto)

        sb.AppendLine("<!DOCTYPE html>")
        sb.AppendLine("<html>")
        sb.AppendLine("<head>")
        sb.AppendLine("<meta http-equiv=""Content-Type"" content=""text/html; charset=utf-8"" />")
        sb.AppendLine("<meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />")
        sb.AppendLine("<style type=""text/css"">")
        sb.AppendLine("  body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f1f5f9; margin: 0; padding: 0; color: #1e293b; }")
        sb.AppendLine("  .wrapper-table { width: 100%; background-color: #f1f5f9; border-collapse: collapse; }")
        sb.AppendLine("  .main-card { max-width: 960px; margin: 0 auto; background-color: #ffffff; border-radius: 8px; overflow: hidden; border: 1px solid #cbd5e1; }")
        sb.AppendLine("  .main-header { background-color: #1e40af; background: linear-gradient(135deg, #1e40af 0%, #3b82f6 100%); color: #ffffff; padding: 22px 28px; text-align: left; }")
        sb.AppendLine("  .main-header h1 { margin: 0 0 6px 0; font-size: 20px; font-weight: 800; letter-spacing: -0.5px; color: #ffffff !important; }")
        sb.AppendLine("  .main-header p { margin: 0; font-size: 13px; color: #dbeafe !important; }")
        sb.AppendLine("  .kpi-banner { width: 100%; border-collapse: collapse; background-color: #f8fafc; border-bottom: 2px solid #e2e8f0; text-align: center; }")
        sb.AppendLine("  .kpi-cell { padding: 12px 10px; border-right: 1px solid #e2e8f0; }")
        sb.AppendLine("  .kpi-label { font-size: 10px; text-transform: uppercase; font-weight: 700; letter-spacing: 0.5px; color: #64748b; margin-bottom: 4px; }")
        sb.AppendLine("  .kpi-value { font-size: 18px; font-weight: 800; }")
        sb.AppendLine("  .kpi-val-tot { color: #1e293b; }")
        sb.AppendLine("  .kpi-val-grn { color: #15803d; }")
        sb.AppendLine("  .kpi-val-yel { color: #b45309; }")
        sb.AppendLine("  .kpi-val-red { color: #b91c1c; }")
        sb.AppendLine("  .kpi-val-mto { color: #0284c7; font-size: 13px; }")
        sb.AppendLine("  .container { padding: 20px 24px; background-color: #ffffff; }")
        sb.AppendLine("  .sec-heading { font-size: 15px; font-weight: 700; color: #1e3a8a; margin: 24px 0 8px 0; padding-bottom: 6px; border-bottom: 2px solid #e2e8f0; }")
        sb.AppendLine("  .sec-subtext { font-size: 12px; color: #64748b; margin: 0 0 14px 0; }")
        sb.AppendLine("  .badge-v { display: inline-block; background-color: #dcfce7; color: #15803d; border: 1px solid #86efac; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px; }")
        sb.AppendLine("  .badge-a { display: inline-block; background-color: #fef9c3; color: #a16207; border: 1px solid #fde047; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px; }")
        sb.AppendLine("  .badge-r { display: inline-block; background-color: #fee2e2; color: #b91c1c; border: 1px solid #fca5a5; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px; }")
        sb.AppendLine("  .card-prio { border-left: 5px solid #dc2626; background-color: #fff1f2; padding: 12px 16px; margin-bottom: 12px; border-radius: 4px; border-top: 1px solid #e2e8f0; border-right: 1px solid #e2e8f0; border-bottom: 1px solid #e2e8f0; }")
        sb.AppendLine("  .card-prio-meta { font-size: 11px; color: #475569; line-height: 1.5; }")
        sb.AppendLine("  .footer { background-color: #f8fafc; border-top: 1px solid #e2e8f0; padding: 16px 24px; font-size: 11px; color: #64748b; text-align: center; }")
        sb.AppendLine("</style>")
        sb.AppendLine("</head>")
        sb.AppendLine("<body style=""margin: 0; padding: 0; background-color: #f1f5f9; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; color: #1e293b;"">")
        sb.AppendLine("<table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""#f1f5f9"" class=""wrapper-table"" style=""width: 100%; border-collapse: collapse; background-color: #f1f5f9; margin: 0; padding: 0;"">")
        sb.AppendLine("  <tr>")
        sb.AppendLine("    <td align=""center"" style=""padding: 16px 8px; background-color: #f1f5f9;"">")
        sb.AppendLine("      <table role=""presentation"" align=""center"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""#ffffff"" class=""main-card"" style=""max-width: 960px; width: 100%; margin: 0 auto; background-color: #ffffff; border-radius: 8px; border: 1px solid #cbd5e1; border-collapse: separate; overflow: hidden;"">")
        sb.AppendLine("        <tr>")
        sb.AppendLine("          <td align=""left"" bgcolor=""#ffffff"" style=""background-color: #ffffff; padding: 0;"">")
        sb.AppendLine("")
        sb.AppendLine("            <!-- 1. Header principal corporativo -->")
        sb.AppendLine("            <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""#1e40af"" class=""main-header"" style=""width: 100%; border-collapse: collapse; background-color: #1e40af; background: linear-gradient(135deg, #1e40af 0%, #3b82f6 100%);"">")
        sb.AppendLine("              <tr>")
        sb.AppendLine("                <td bgcolor=""#1e40af"" style=""padding: 22px 28px; background-color: #1e40af; text-align: left;"">")
        sb.AppendLine("                  <h1 style=""margin: 0 0 6px 0; font-size: 20px; font-weight: 800; letter-spacing: -0.5px; color: #ffffff !important;"">Informe Diario de Seguimiento Comercial</h1>")
        sb.AppendLine(String.Format("                  <p style=""margin: 0; font-size: 13px; color: #dbeafe !important;"">Vendedor: <strong>{0}</strong> ({1}) &bull; Emitido el {2:dd/MM/yyyy HH:mm:ss} &bull; Horario 08:15 AM</p>",
                                                    System.Net.WebUtility.HtmlEncode(resumen.VendedorNombre), resumen.VendedorClave, DateTime.Now))
        sb.AppendLine("                </td>")
        sb.AppendLine("              </tr>")
        sb.AppendLine("            </table>")
        sb.AppendLine("")
        sb.AppendLine("            <!-- 2. Banner de Indicadores Clave (KPI) -->")
        sb.AppendLine("            <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""#f8fafc"" class=""kpi-banner"" style=""width: 100%; border-collapse: collapse; background-color: #f8fafc; border-bottom: 2px solid #e2e8f0; text-align: center;"">")
        sb.AppendLine("              <tr>")
        sb.AppendLine(String.Format("                <td class=""kpi-cell"" bgcolor=""#f8fafc"" style=""padding: 12px 10px; border-right: 1px solid #e2e8f0; text-align: center;""><div class=""kpi-label"" style=""font-size: 10px; text-transform: uppercase; font-weight: 700; letter-spacing: 0.5px; color: #64748b; margin-bottom: 4px;"">Cotiz. Cliente (Pendientes OC)</div><div class=""kpi-value kpi-val-tot"" style=""font-size: 18px; font-weight: 800; color: #1e293b;"">{0}</div></td>", totalCliente))
        sb.AppendLine(String.Format("                <td class=""kpi-cell"" bgcolor=""#f8fafc"" style=""padding: 12px 10px; border-right: 1px solid #e2e8f0; text-align: center;""><div class=""kpi-label"" style=""font-size: 10px; text-transform: uppercase; font-weight: 700; letter-spacing: 0.5px; color: #64748b; margin-bottom: 4px;"">Cotiz. Internas (Pendientes Cliente)</div><div class=""kpi-value kpi-val-tot"" style=""font-size: 18px; font-weight: 800; color: #1e293b;"">{0}</div></td>", totalInternas))
        sb.AppendLine(String.Format("                <td class=""kpi-cell"" bgcolor=""#f8fafc"" style=""padding: 12px 10px; border-right: 1px solid #e2e8f0; text-align: center;""><div class=""kpi-label"" style=""font-size: 10px; text-transform: uppercase; font-weight: 700; letter-spacing: 0.5px; color: #64748b; margin-bottom: 4px;"">Críticos (Rojos)</div><div class=""kpi-value kpi-val-red"" style=""font-size: 18px; font-weight: 800; color: #b91c1c;"">{0}</div></td>", rojosCount))
        sb.AppendLine(String.Format("                <td class=""kpi-cell"" bgcolor=""#f8fafc"" style=""padding: 12px 10px; border-right: 1px solid #e2e8f0; text-align: center;""><div class=""kpi-label"" style=""font-size: 10px; text-transform: uppercase; font-weight: 700; letter-spacing: 0.5px; color: #64748b; margin-bottom: 4px;"">Advertencia (Amarillos)</div><div class=""kpi-value kpi-val-yel"" style=""font-size: 18px; font-weight: 800; color: #b45309;"">{0}</div></td>", amarillosCount))
        sb.AppendLine(String.Format("                <td class=""kpi-cell"" bgcolor=""#f8fafc"" style=""padding: 12px 10px; border-right: 1px solid #e2e8f0; text-align: center;""><div class=""kpi-label"" style=""font-size: 10px; text-transform: uppercase; font-weight: 700; letter-spacing: 0.5px; color: #64748b; margin-bottom: 4px;"">En Tiempo (Verdes)</div><div class=""kpi-value kpi-val-grn"" style=""font-size: 18px; font-weight: 800; color: #15803d;"">{0}</div></td>", verdesCount))
        sb.AppendLine(String.Format("                <td class=""kpi-cell"" bgcolor=""#f8fafc"" style=""padding: 12px 10px; border-right: none; text-align: center;""><div class=""kpi-label"" style=""font-size: 10px; text-transform: uppercase; font-weight: 700; letter-spacing: 0.5px; color: #64748b; margin-bottom: 4px;"">Monto en Cartera</div><div class=""kpi-value kpi-val-mto"" style=""font-size: 13px; font-weight: 800; color: #0284c7;"">${0:N2} USD<br/><span style=""font-size: 11px; color: #475569; font-weight: 600;"">${1:N2} MXN</span></div></td>", totalUSD, totalMXN))
        sb.AppendLine("              </tr>")
        sb.AppendLine("            </table>")
        sb.AppendLine("")
        sb.AppendLine("            <div class=""container"" style=""padding: 20px 24px; background-color: #ffffff;"">")
        sb.AppendLine("")
        sb.AppendLine("              <!-- ========================================================================= -->")
        sb.AppendLine("              <!-- 1. COTIZACIONES DE CLIENTES PENDIENTES DE ORDEN DE COMPRA                   -->")
        sb.AppendLine("              <!-- ========================================================================= -->")
        sb.AppendLine(String.Format("              <div class=""sec-heading"" style=""font-size: 15px; font-weight: 700; color: #1e3a8a; margin: 12px 0 6px 0; padding-bottom: 6px; border-bottom: 2px solid #e2e8f0;"">&#128203; 1. Cotizaciones de Clientes Pendientes de Orden de Compra ({0})</div>", totalCliente))
        sb.AppendLine("              <p class=""sec-subtext"" style=""font-size: 12px; color: #64748b; margin: 0 0 14px 0;"">Cotizaciones formales entregadas al cliente en espera de confirmación y recepción de la Orden de Compra formal para proceder con el suministro.</p>")
        sb.AppendLine("")

        If totalCliente = 0 Then
            sb.AppendLine("              <div style=""font-size: 12px; color: #166534; background-color: #dcfce7; border: 1px solid #86efac; padding: 12px; border-radius: 6px; margin-bottom: 18px;"">&#10004; No tiene cotizaciones de clientes pendientes de orden de compra en este momento.</div>")
        Else
            For Each itm In resumen.CotizacionesCliente
                sb.Append(GenerarTarjetaSeguimientoHtml(itm))
            Next
        End If

        sb.AppendLine("")
        sb.AppendLine("              <!-- ========================================================================= -->")
        sb.AppendLine("              <!-- 2. COTIZACIONES INTERNAS PENDIENTES DE GENERAR COTIZACIÓN A CLIENTE         -->")
        sb.AppendLine("              <!-- ========================================================================= -->")
        sb.AppendLine(String.Format("              <div class=""sec-heading"" style=""font-size: 15px; font-weight: 700; color: #1e3a8a; margin: 24px 0 6px 0; padding-bottom: 6px; border-bottom: 2px solid #e2e8f0;"">&#9201; 2. Cotizaciones Internas Pendientes de Generar Cotización a Cliente ({0})</div>", totalInternas))
        sb.AppendLine("              <p class=""sec-subtext"" style=""font-size: 12px; color: #64748b; margin: 0 0 14px 0;"">Cotizaciones internas elaboradas por compras con costos disponibles. Demandan generar y enviar formalmente la cotización de venta al cliente a la brevedad.</p>")
        sb.AppendLine("")

        If totalInternas = 0 Then
            sb.AppendLine("              <div style=""font-size: 12px; color: #166534; background-color: #dcfce7; border: 1px solid #86efac; padding: 12px; border-radius: 6px; margin-bottom: 18px;"">&#10004; No tiene cotizaciones internas pendientes de cotizar a cliente en este momento.</div>")
        Else
            For Each itm In resumen.CotizacionesInternas
                sb.Append(GenerarTarjetaSeguimientoHtml(itm))
            Next
        End If

        sb.AppendLine("            </div>")
        sb.AppendLine("")
        sb.AppendLine("            <!-- Footer institucional -->")
        sb.AppendLine("            <div class=""footer"" style=""background-color: #f8fafc; border-top: 1px solid #e2e8f0; padding: 16px 24px; font-size: 11px; color: #64748b; text-align: center;"">")
        sb.AppendLine("              HistoMedic Robot LFM IA &bull; Rutina automática matutina de seguimiento comercial a ventas (08:15 AM Lunes a Viernes).<br/>")
        sb.AppendLine("              Por favor no responda directamente a este correo automático; para cualquier actualización registre el seguimiento en el sistema.")
        sb.AppendLine("            </div>")
        sb.AppendLine("")
        sb.AppendLine("          </td>")
        sb.AppendLine("        </tr>")
        sb.AppendLine("      </table>")
        sb.AppendLine("    </td>")
        sb.AppendLine("  </tr>")
        sb.AppendLine("</table>")
        sb.AppendLine("</body>")
        sb.AppendLine("</html>")

        Return sb.ToString()
    End Function

#End Region

#Region "Notificaciones Seguimiento Compras (08:20 AM)"

    ''' <summary>
    ''' Modelo que representa una oportunidad de venta en proceso de cotización para compras.
    ''' </summary>
    Public Class ItemOportunidadSeguimientoCompras
        Public Property VentaId As Integer
        Public Property ProyectoId As String
        Public Property Titulo As String
        Public Property ClasificacionId As Integer
        Public Property ClasificacionNombre As String
        Public Property VendedorClave As String
        Public Property VendedorNombre As String
        Public Property ClienteNombre As String
        Public Property ClienteFinal As String
        Public Property FechaProyecto As Nullable(Of DateTime)
        Public Property FechaRegistro As DateTime
        Public Property TotalMonto As Double
        Public Property MonedaSiglas As String
        Public Property TotalSolicitudesProveedor As Integer
        Public Property SolicitudesProveedorEnviadas As Integer
        Public Property TotalPartidasSolicitadas As Integer
        Public Property PartidasConPrecio As Integer
        Public Property PartidasSinPrecio As Integer
        Public Property ProveedoresNombres As String
        Public Property DiasTranscurridos As Integer
        Public Property Semaforo As String ' ROJO, AMARILLO, VERDE
        Public Property BadgeDiasTexto As String
        Public Property QueEstaPendiente As String
        Public Property Responsable As String
        Public Property ProximaAccion As String

        Public ReadOnly Property TieneSolicitudProveedor As Boolean
            Get
                Return TotalSolicitudesProveedor > 0
            End Get
        End Property
    End Class

    ''' <summary>
    ''' Resumen agrupado para el reporte de compras (FLOWSERVE o DIVERSOS).
    ''' </summary>
    Public Class ResumenComprasSeguimiento
        Public Property GrupoNombre As String
        Public Property Destinatarios As String
        Public Property OportunidadesSinSolicitud As New List(Of ItemOportunidadSeguimientoCompras)()
        Public Property OportunidadesEnCotizacion As New List(Of ItemOportunidadSeguimientoCompras)()

        Public ReadOnly Property TotalOportunidades As Integer
            Get
                Return OportunidadesSinSolicitud.Count + OportunidadesEnCotizacion.Count
            End Get
        End Property
    End Class

    ''' <summary>
    ''' Registra en tb_seguimiento_compras_envio_log el resultado del envío diario.
    ''' </summary>
    Private Sub RegistrarLogEnvioCompras(ByVal grupo As String, ByVal destinatarios As String,
                                         ByVal totalOportunidades As Integer, ByVal totalSinSol As Integer,
                                         ByVal totalEnProceso As Integer, ByVal enviado As Boolean, ByVal mensajeError As String)
        Try
            Dim sqlInsert As String = String.Format(
                "INSERT INTO tb_seguimiento_compras_envio_log (fecha, grupo, destinatarios, total_oportunidades, total_sin_solicitud, total_en_proceso, enviado, fchregistro, mensaje_error) " & _
                "VALUES (CURDATE(), '{0}', '{1}', {2}, {3}, {4}, {5}, NOW(), '{6}');",
                grupo.Replace("'", "''"), destinatarios.Replace("'", "''"),
                totalOportunidades, totalSinSol, totalEnProceso, If(enviado, 1, 0), mensajeError.Replace("'", "''"))
            Using cmm As New MySqlConnector.MySqlCommand(sqlInsert, cx_MySQL_local)
                cmm.ExecuteNonQuery()
            End Using
        Catch ex As Exception
            LogEventos.Escribir("Error en RegistrarLogEnvioCompras: " & ex.Message)
        End Try
    End Sub

    ''' <summary>
    ''' Rutina matutina programada para enviarse de lunes a viernes a las 8:20 AM al equipo de compras
    ''' con el seguimiento de oportunidades de venta que aún no han sido terminadas de cotizar (FLOWSERVE y DIVERSOS).
    ''' </summary>
    Public Sub NotificarSeguimientoOportunidadesCompras(Optional ByVal forzarEnvio As Boolean = False)
        If _procesandoSeguimientoCompras Then Return

        ' 1. Omitir sábados y domingos (salvo si es forzado manualmente para pruebas)
        If Not forzarEnvio Then
            If DateTime.Now.DayOfWeek = DayOfWeek.Saturday OrElse DateTime.Now.DayOfWeek = DayOfWeek.Sunday Then
                Return
            End If

            ' 2. Validar horario de envío diario (a partir de las 08:20 AM)
            Dim horaProgramada As New TimeSpan(8, 20, 0)
            If DateTime.Now.TimeOfDay < horaProgramada Then
                Return
            End If
        End If

        _procesandoSeguimientoCompras = True
        Try
            If cx_MySQL_local.State <> ConnectionState.Open Then
                Try
                    If cx_MySQL_local.State = ConnectionState.Broken Then cx_MySQL_local.Close()
                    cx_MySQL_local.Open()
                Catch exConn As Exception
                    AgregarLog(500, "[Seguimiento Compras] Error al conectar a la BD local: " & exConn.Message)
                    Return
                End Try
            End If

            ' A) Procesar Grupo FLOWSERVE: clasificacion_proyecto_id IN (2, 3, 4, 6)
            ProcesarSeguimientoComprasGrupo("FLOWSERVE", "correos_segcot_compras_flowserve", New Integer() {2, 3, 4, 6}, _fechaUltimoEnvioSeguimientoComprasFlowserve, forzarEnvio)

            ' B) Procesar Grupo DIVERSOS: clasificacion_proyecto_id IN (5, 6)
            ProcesarSeguimientoComprasGrupo("DIVERSOS", "correos_segcot_compras_diversos", New Integer() {5, 6}, _fechaUltimoEnvioSeguimientoComprasDiversos, forzarEnvio)

        Catch ex As Exception
            AgregarLog(500, "Error en NotificarSeguimientoOportunidadesCompras: " & ex.Message)
            LogEventos.Escribir("Error en NotificarSeguimientoOportunidadesCompras: " & ex.Message & " - Stack: " & ex.StackTrace)
        Finally
            _procesandoSeguimientoCompras = False
        End Try
    End Sub

    ''' <summary>
    ''' Permite ejecutar o probar bajo demanda el informe de FLOWSERVE.
    ''' </summary>
    Public Sub NotificarSeguimientoComprasFlowserve(Optional ByVal forzarEnvio As Boolean = False)
        ProcesarSeguimientoComprasGrupo("FLOWSERVE", "correos_segcot_compras_flowserve", New Integer() {2, 3, 4, 6}, _fechaUltimoEnvioSeguimientoComprasFlowserve, forzarEnvio)
    End Sub

    ''' <summary>
    ''' Permite ejecutar o probar bajo demanda el informe de DIVERSOS.
    ''' </summary>
    Public Sub NotificarSeguimientoComprasDiversos(Optional ByVal forzarEnvio As Boolean = False)
        ProcesarSeguimientoComprasGrupo("DIVERSOS", "correos_segcot_compras_diversos", New Integer() {5, 6}, _fechaUltimoEnvioSeguimientoComprasDiversos, forzarEnvio)
    End Sub

    ''' <summary>
    ''' Procesa y envía el informe de seguimiento de compras para un grupo específico (FLOWSERVE o DIVERSOS).
    ''' </summary>
    Private Sub ProcesarSeguimientoComprasGrupo(ByVal tipoGrupo As String, ByVal campoDestinatarios As String,
                                                ByVal clasificacionesIds As Integer(),
                                                ByRef fechaUltimoEnvio As Nullable(Of DateTime),
                                                ByVal forzarEnvio As Boolean)
        Try
            ' 1. Validar si ya fue enviado hoy este grupo
            If Not forzarEnvio Then
                If fechaUltimoEnvio.HasValue AndAlso fechaUltimoEnvio.Value.Date = DateTime.Now.Date Then
                    Return
                End If

                Dim sqlCheck As String = String.Format(
                    "SELECT COUNT(*) FROM tb_seguimiento_compras_envio_log WHERE fecha = CURDATE() AND grupo = '{0}' AND enviado = 1",
                    tipoGrupo.Replace("'", "''"))
                Dim dtCheck As DataTable = tb_Recordset_MySQL_local(sqlCheck)
                If dtCheck IsNot Nothing AndAlso dtCheck.Rows.Count > 0 AndAlso Convert.ToInt32(dtCheck.Rows(0)(0)) > 0 Then
                    Return
                End If
            End If

            ' 2. Obtener destinatarios y directivos en CC desde cat_consultorio
            Dim destinatarios As String = ""
            Dim correosDirectivos As String = ""
            Dim dtConfig As DataTable = tb_Recordset_MySQL_local(String.Format("SELECT {0}, correos_solo_directivos FROM cat_consultorio LIMIT 1", campoDestinatarios))
            If dtConfig IsNot Nothing AndAlso dtConfig.Rows.Count > 0 Then
                If Not IsDBNull(dtConfig.Rows(0)(campoDestinatarios)) Then
                    destinatarios = dtConfig.Rows(0)(campoDestinatarios).ToString().Trim()
                End If
                If dtConfig.Columns.Contains("correos_solo_directivos") AndAlso Not IsDBNull(dtConfig.Rows(0)("correos_solo_directivos")) Then
                    correosDirectivos = dtConfig.Rows(0)("correos_solo_directivos").ToString().Trim()
                End If
            End If

            If String.IsNullOrWhiteSpace(destinatarios) Then
                AgregarLog(500, String.Format("[Seguimiento Compras - {0}] No hay destinatarios configurados en cat_consultorio.{1}.", tipoGrupo, campoDestinatarios))
                Return
            End If

            ' 3. Consultar y construir resumen de oportunidades en proceso de cotización
            Dim resumen As ResumenComprasSeguimiento = ObtenerResumenOportunidadesCompras(tipoGrupo, clasificacionesIds)
            resumen.Destinatarios = destinatarios

            If resumen.TotalOportunidades = 0 Then
                LogEventos.Escribir(String.Format("[Seguimiento Compras - {0}] No hay oportunidades de venta pendientes de cotizar a partir del 24/08/2026.", tipoGrupo))
                Return
            End If

            ' 4. Generar HTML con diseño idéntico al provisto
            Dim htmlCuerpo As String = GenerarHTMLSeguimientoCompras(resumen, tipoGrupo)
            Dim asunto As String = String.Format("Informe Diario de Seguimiento a Compras - {0} ({1:dd/MM/yyyy})", tipoGrupo, DateTime.Now)

            ' Guardar respaldo local del HTML para auditoría
            Try
                Dim rutaHtmlLocal As String = System.IO.Path.Combine(Application.StartupPath, "UltimoSeguimientoCompras_" & tipoGrupo & ".html")
                System.IO.File.WriteAllText(rutaHtmlLocal, htmlCuerpo, System.Text.Encoding.UTF8)
            Catch exFile As Exception
            End Try

            ' 5. Enviar correo HTML mediante Chilkat con copia (CC) a directivos
            Dim enviadoExitoso As Boolean = EnviarCorreoNotificacionHTML(destinatarios, asunto, htmlCuerpo, correosDirectivos)
            If enviadoExitoso Then
                fechaUltimoEnvio = DateTime.Now
                'AgregarLog(100, String.Format("[Seguimiento Compras - {0}] Notificación enviada a {1}{2} - Total: {3} (Sin Solicitud: {4}, En Cotización: {5}).",
                '                              tipoGrupo, destinatarios, If(Not String.IsNullOrWhiteSpace(correosDirectivos), " [CC: " & correosDirectivos & "]", ""),
                '                              resumen.TotalOportunidades, resumen.OportunidadesSinSolicitud.Count, resumen.OportunidadesEnCotizacion.Count))
                RegistrarLogEnvioCompras(tipoGrupo, destinatarios, resumen.TotalOportunidades, resumen.OportunidadesSinSolicitud.Count, resumen.OportunidadesEnCotizacion.Count, True, "Enviado con éxito")
            Else
                AgregarLog(500, String.Format("[Seguimiento Compras - {0}] Error al enviar correo a {1}.", tipoGrupo, destinatarios))
                RegistrarLogEnvioCompras(tipoGrupo, destinatarios, resumen.TotalOportunidades, resumen.OportunidadesSinSolicitud.Count, resumen.OportunidadesEnCotizacion.Count, False, "Error al enviar mediante Chilkat")
            End If

        Catch ex As Exception
            AgregarLog(500, String.Format("Error en ProcesarSeguimientoComprasGrupo ({0}): {1}", tipoGrupo, ex.Message))
            LogEventos.Escribir(String.Format("Error en ProcesarSeguimientoComprasGrupo ({0}): {1} - Stack: {2}", tipoGrupo, ex.Message, ex.StackTrace))
        End Try
    End Sub

    ''' <summary>
    ''' Consulta y procesa las oportunidades de venta para compras con v.enviada = 1, activo = 'ACTIVO',
    ''' fecha >= '2026-08-24', estatus_proyecto_id IN (1, 3) y sin cotización interna elaborada.
    ''' </summary>
    Private Function ObtenerResumenOportunidadesCompras(ByVal tipoGrupo As String, ByVal clasificacionesIds As Integer()) As ResumenComprasSeguimiento
        Dim resumen As New ResumenComprasSeguimiento()
        resumen.GrupoNombre = tipoGrupo

        Dim inClasif As String = String.Join(",", clasificacionesIds)
        Dim sqlOportunidades As String =
            "SELECT " & _
            "  v.id AS venta_id, " & _
            "  COALESCE(v.proyecto_id, '') AS proyecto_id, " & _
            "  COALESCE(v.titulo, '') AS titulo, " & _
            "  v.fecha AS fecha_proyecto, " & _
            "  v.fchregistro AS fecha_registro, " & _
            "  COALESCE(cp.id, 0) AS clasificacion_id, " & _
            "  COALESCE(cp.clasificacion, 'SIN CLASIFICACIÓN') AS clasificacion_nombre, " & _
            "  COALESCE(v.ccveusuario_vendedor, '') AS ccveusuario_vendedor, " & _
            "  COALESCE(TRIM(CONCAT_WS(' ', m.cnombre, m.cpriapellido, m.csegapellido)), v.ccveusuario_vendedor, 'SIN ASIGNAR') AS vendedor_nombre, " & _
            "  COALESCE(cli.nombre_comercial, cli.razon_social, 'CLIENTE NO DEFINIDO') AS cliente_nombre, " & _
            "  COALESCE(v.cliente_final, '') AS cliente_final, " & _
            "  COALESCE(v.total, 0) AS total_monto, " & _
            "  COALESCE(tc.siglas, 'USD') AS moneda_siglas, " & _
            "  (SELECT COUNT(*) FROM tb_compras_cotizaciones c WHERE c.venta_id = v.id) AS total_sols_prov, " & _
            "  (SELECT COUNT(*) FROM tb_compras_cotizaciones c WHERE c.venta_id = v.id AND c.enviado = 1) AS sols_prov_enviadas, " & _
            "  (SELECT COUNT(*) FROM tb_compras_cotizaciones c JOIN tb_compras_cotizaciones_detalle cd ON c.id = cd.cotizacion_id WHERE c.venta_id = v.id) AS total_partidas_sol, " & _
            "  (SELECT COALESCE(SUM(CASE WHEN cd.precio_unitario > 0 THEN 1 ELSE 0 END), 0) FROM tb_compras_cotizaciones c JOIN tb_compras_cotizaciones_detalle cd ON c.id = cd.cotizacion_id WHERE c.venta_id = v.id) AS partidas_con_precio, " & _
            "  (SELECT COALESCE(SUM(CASE WHEN cd.precio_unitario = 0 OR cd.precio_unitario IS NULL THEN 1 ELSE 0 END), 0) FROM tb_compras_cotizaciones c JOIN tb_compras_cotizaciones_detalle cd ON c.id = cd.cotizacion_id WHERE c.venta_id = v.id) AS partidas_sin_precio, " & _
            "  (SELECT GROUP_CONCAT(DISTINCT COALESCE(p.cDatGenNombreAbreviado, p.cDatGenRazonSocial) SEPARATOR ', ') FROM tb_compras_cotizaciones c LEFT JOIN tb_proveedores p ON c.proveedor_id = p.icveProveedor WHERE c.venta_id = v.id) AS proveedores_nombres " & _
            "FROM tb_ventas v " & _
            "LEFT JOIN cat_clientes cli ON v.cliente_id = cli.id " & _
            "LEFT JOIN cat_clasificacion_proyectos cp ON v.clasificacion_proyecto_id = cp.id " & _
            "LEFT JOIN cat_medico m ON v.ccveusuario_vendedor = m.ccvemedico " & _
            "LEFT JOIN cat_tipos_cambio tc ON v.moneda_id = tc.id " & _
            "WHERE v.enviada = 1 " & _
            "  AND v.activo = 'ACTIVO' " & _
            "  AND v.fecha >= '2026-08-24' " & _
            "  AND v.estatus_proyecto_id IN (1, 3) " & _
            "  AND NOT EXISTS (SELECT 1 FROM tb_compras_cotizacion_interna ci WHERE ci.venta_id = v.id) " & _
            "  AND v.clasificacion_proyecto_id IN (" & inClasif & ") " & _
            "ORDER BY v.fecha ASC, total_monto DESC;"

        Dim dt As DataTable = tb_Recordset_MySQL_local(sqlOportunidades)
        If dt Is Nothing Then Return resumen

        For Each r As DataRow In dt.Rows
            Dim item As New ItemOportunidadSeguimientoCompras()
            item.VentaId = Convert.ToInt32(r("venta_id"))
            item.ProyectoId = r("proyecto_id").ToString().Trim()
            item.Titulo = r("titulo").ToString().Trim()
            item.ClasificacionId = Convert.ToInt32(r("clasificacion_id"))
            item.ClasificacionNombre = r("clasificacion_nombre").ToString().Trim()
            item.VendedorClave = r("ccveusuario_vendedor").ToString().Trim()
            item.VendedorNombre = r("vendedor_nombre").ToString().Trim()
            item.ClienteNombre = r("cliente_nombre").ToString().Trim()
            item.ClienteFinal = r("cliente_final").ToString().Trim()
            item.TotalMonto = If(Not IsDBNull(r("total_monto")), Convert.ToDouble(r("total_monto")), 0)
            item.MonedaSiglas = If(Not IsDBNull(r("moneda_siglas")), r("moneda_siglas").ToString().Trim(), "USD")
            item.TotalSolicitudesProveedor = Convert.ToInt32(r("total_sols_prov"))
            item.SolicitudesProveedorEnviadas = Convert.ToInt32(r("sols_prov_enviadas"))
            item.TotalPartidasSolicitadas = Convert.ToInt32(r("total_partidas_sol"))
            item.PartidasConPrecio = Convert.ToInt32(r("partidas_con_precio"))
            item.PartidasSinPrecio = Convert.ToInt32(r("partidas_sin_precio"))
            item.ProveedoresNombres = If(Not IsDBNull(r("proveedores_nombres")), r("proveedores_nombres").ToString().Trim(), "")

            ' Fecha del proyecto y fecha de registro
            If Not IsDBNull(r("fecha_proyecto")) Then
                Dim fPry As DateTime
                If DateTime.TryParse(r("fecha_proyecto").ToString(), fPry) Then
                    item.FechaProyecto = fPry
                End If
            End If
            If Not IsDBNull(r("fecha_registro")) Then
                Dim fReg As DateTime
                If DateTime.TryParse(r("fecha_registro").ToString(), fReg) Then
                    item.FechaRegistro = fReg
                Else
                    item.FechaRegistro = DateTime.Now
                End If
            Else
                item.FechaRegistro = DateTime.Now
            End If

            ' Cálculo del tiempo transcurrido en relación a la fecha de proyecto de venta
            Dim fechaBase As DateTime = If(item.FechaProyecto.HasValue, item.FechaProyecto.Value, item.FechaRegistro)
            Dim dTrans As Integer = CInt(Math.Floor((DateTime.Now.Date - fechaBase.Date).TotalDays))
            If dTrans < 0 Then dTrans = 0
            item.DiasTranscurridos = dTrans

            ' =========================================================
            ' Evaluación de Semáforo y Textos Descriptivos
            ' =========================================================
            If Not item.TieneSolicitudProveedor Then
                ' Caso 1: Aún no se realiza solicitud de cotización a proveedor
                If dTrans <= 1 Then
                    item.Semaforo = "VERDE"
                    item.BadgeDiasTexto = If(dTrans = 0, "&#9203; Recibida HOY", "&#9203; Recibida hace 1 día") & " &bull; Sin solicitud"
                    item.QueEstaPendiente = String.Format("Oportunidad enviada a compras el {0:dd/MM/yyyy}. Recién ingresada, pendiente emitir solicitud formal a proveedor.", fechaBase)
                    item.Responsable = String.Format("Compras &bull; Vendedor: {0}", item.VendedorNombre)
                    item.ProximaAccion = "Emitir de inmediato solicitud de cotización formal a proveedor(es) en el sistema."
                ElseIf dTrans <= 3 Then
                    item.Semaforo = "AMARILLO"
                    item.BadgeDiasTexto = String.Format("&#9203; {0} días sin solicitar a proveedor", dTrans)
                    item.QueEstaPendiente = String.Format("Han transcurrido {0} días desde la fecha del proyecto ({1:dd/MM/yyyy}) sin registrar ninguna solicitud de cotización a proveedor.", dTrans, fechaBase)
                    item.Responsable = String.Format("Compras &bull; Vendedor: {0}", item.VendedorNombre)
                    item.ProximaAccion = "Emitir con prioridad la solicitud de cotización a proveedores para evitar demoras comerciales."
                Else
                    item.Semaforo = "ROJO"
                    item.BadgeDiasTexto = String.Format("&#9888; {0} días sin solicitar a proveedor", dTrans)
                    item.QueEstaPendiente = String.Format("Retraso crítico: Han transcurrido {0} días desde la fecha del proyecto ({1:dd/MM/yyyy}) sin realizar solicitud de cotización a ningún proveedor.", dTrans, fechaBase)
                    item.Responsable = String.Format("Compras &bull; Vendedor: {0}", item.VendedorNombre)
                    item.ProximaAccion = "Generar de forma urgente la solicitud a proveedores y registrarla en el sistema para detonar el proceso de cotización."
                End If

                resumen.OportunidadesSinSolicitud.Add(item)
            Else
                ' Caso 2: Tiene solicitudes a proveedores pero está en proceso de cotización
                Dim provTxt As String = If(Not String.IsNullOrWhiteSpace(item.ProveedoresNombres), " con " & item.ProveedoresNombres, "")
                Dim partTxt As String = If(item.TotalPartidasSolicitadas > 0, String.Format(" ({0} partidas solicitadas: {1} cotizadas / {2} pendientes)", item.TotalPartidasSolicitadas, item.PartidasConPrecio, item.PartidasSinPrecio), "")

                If dTrans <= 3 Then
                    item.Semaforo = "VERDE"
                    item.BadgeDiasTexto = String.Format("&#9203; {0} días transcurridos ({1} sol. prov)", dTrans, item.TotalSolicitudesProveedor)
                    item.QueEstaPendiente = String.Format("En proceso de cotización{0}{1}. Han transcurrido {2} días desde la fecha del proyecto ({3:dd/MM/yyyy}). En tiempo normal.", provTxt, partTxt, dTrans, fechaBase)
                    item.Responsable = String.Format("Compras{0} &bull; Vendedor: {1}", If(Not String.IsNullOrWhiteSpace(item.ProveedoresNombres), " (Proveedor: " & item.ProveedoresNombres & ")", ""), item.VendedorNombre)
                    item.ProximaAccion = "Monitorear fecha de respuesta de proveedores y registrar cotizaciones recibidas."
                ElseIf dTrans <= 7 Then
                    item.Semaforo = "AMARILLO"
                    item.BadgeDiasTexto = String.Format("&#9203; {0} días en cotización ({1} sol. prov)", dTrans, item.TotalSolicitudesProveedor)
                    item.QueEstaPendiente = String.Format("Han transcurrido {0} días desde la fecha del proyecto ({1:dd/MM/yyyy}). Proveedores en proceso de cotización{2}{3}.", dTrans, fechaBase, provTxt, partTxt)
                    item.Responsable = String.Format("Compras{0} &bull; Vendedor: {1}", If(Not String.IsNullOrWhiteSpace(item.ProveedoresNombres), " (Proveedor: " & item.ProveedoresNombres & ")", ""), item.VendedorNombre)
                    item.ProximaAccion = "Dar seguimiento activo por correo/teléfono a cotizaciones de proveedores y solicitar respuestas pendientes."
                Else
                    item.Semaforo = "ROJO"
                    item.BadgeDiasTexto = String.Format("&#9888; {0} días en cotización ({1} sol. prov)", dTrans, item.TotalSolicitudesProveedor)
                    item.QueEstaPendiente = String.Format("Cotización demorada: Han transcurrido {0} días desde la fecha del proyecto ({1:dd/MM/yyyy}) sin completar cotización{2}{3}.", dTrans, fechaBase, provTxt, partTxt)
                    item.Responsable = String.Format("Compras{0} &bull; Vendedor: {1}", If(Not String.IsNullOrWhiteSpace(item.ProveedoresNombres), " (Proveedor: " & item.ProveedoresNombres & ")", ""), item.VendedorNombre)
                    item.ProximaAccion = "Gestionar urgentemente escalamiento con proveedores para cierre de precios y proceder a elaborar la cotización interna."
                End If

                resumen.OportunidadesEnCotizacion.Add(item)
            End If
        Next

        Return resumen
    End Function

    ''' <summary>
    ''' Genera la tarjeta HTML individual para una oportunidad de compra, con diseño idéntico a la imagen provista.
    ''' </summary>
    Private Function GenerarTarjetaSeguimientoComprasHtml(ByVal item As ItemOportunidadSeguimientoCompras) As String
        Dim badgeClass As String = If(item.Semaforo = "ROJO", "badge-r", If(item.Semaforo = "AMARILLO", "badge-a", "badge-v"))
        Dim badgeStyle As String = If(item.Semaforo = "ROJO",
            "display: inline-block; background-color: #fee2e2; color: #b91c1c !important; border: 1px solid #fca5a5; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px;",
            If(item.Semaforo = "AMARILLO",
                "display: inline-block; background-color: #fef9c3; color: #a16207 !important; border: 1px solid #fde047; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px;",
                "display: inline-block; background-color: #dcfce7; color: #15803d !important; border: 1px solid #86efac; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px;"))
        Dim borderCol As String = If(item.Semaforo = "ROJO", "#dc2626", If(item.Semaforo = "AMARILLO", "#d97706", "#16a34a"))
        Dim bgCol As String = If(item.Semaforo = "ROJO", "#fff1f2", If(item.Semaforo = "AMARILLO", "#fffbeb", "#f0fdf4"))

        Dim bgDias As String = If(item.Semaforo = "ROJO", "#fee2e2", If(item.Semaforo = "AMARILLO", "#fef9c3", "#dcfce7"))
        Dim colDias As String = If(item.Semaforo = "ROJO", "#b91c1c", If(item.Semaforo = "AMARILLO", "#a16207", "#15803d"))
        Dim borderDias As String = If(item.Semaforo = "ROJO", "#fca5a5", If(item.Semaforo = "AMARILLO", "#fde047", "#86efac"))

        Dim sb As New System.Text.StringBuilder()
        sb.AppendLine(String.Format("    <div class=""card-prio"" style=""border-left: 5px solid {0}; background-color: {1}; padding: 12px 16px; margin-bottom: 12px; border-radius: 4px; border-top: 1px solid #e2e8f0; border-right: 1px solid #e2e8f0; border-bottom: 1px solid #e2e8f0;"">", borderCol, bgCol))
        sb.AppendLine(String.Format("      <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""{0}"" style=""width: 100%; border-collapse: collapse; background-color: {0};"">", bgCol))
        sb.AppendLine("        <tr>")
        sb.AppendLine(String.Format("          <td style=""font-size: 13px; font-weight: 700; color: #0f172a;""><span class=""{0}"" style=""{1}"">&#9679; {2}</span> &nbsp; Folio: <span style=""font-family: Consolas, monospace;"">{3}</span> &bull; {4} &nbsp;<span style=""display: inline-block; background-color: {5}; color: {6} !important; border: 1px solid {7}; padding: 2px 8px; border-radius: 10px; font-weight: 700; font-size: 11px; vertical-align: middle;"">{8}</span></td>",
                                    badgeClass, badgeStyle, item.Semaforo, item.ProyectoId, System.Net.WebUtility.HtmlEncode(item.ClienteNombre), bgDias, colDias, borderDias, item.BadgeDiasTexto))
        sb.AppendLine("        </tr>")
        sb.AppendLine("      </table>")
        sb.AppendLine(String.Format("      <div style=""font-size: 12px; color: #334155; margin: 5px 0;""><strong>Descripción:</strong> {0}</div>", System.Net.WebUtility.HtmlEncode(item.Titulo)))
        sb.AppendLine("      <div class=""card-prio-meta"" style=""font-size: 11px; color: #475569; line-height: 1.5;"">")
        sb.AppendLine(String.Format("        &bull; <strong>Qué está pendiente:</strong> <span style=""color: {0}; font-weight: 600;"">{1}</span><br/>", If(item.Semaforo = "ROJO", "#991b1b", If(item.Semaforo = "AMARILLO", "#a16207", "#15803d")), System.Net.WebUtility.HtmlEncode(item.QueEstaPendiente)))
        sb.AppendLine(String.Format("        &bull; <strong>Quién es responsable:</strong> <span style=""color: #0f172a; font-weight: 600;"">{0}</span><br/>", System.Net.WebUtility.HtmlEncode(item.Responsable)))
        sb.AppendLine(String.Format("        &bull; <strong>Acción recomendada:</strong> <span style=""color: #1e3a8a; font-weight: 600;"">{0}</span>", System.Net.WebUtility.HtmlEncode(item.ProximaAccion)))
        sb.AppendLine("      </div>")
        sb.AppendLine("    </div>")

        Return sb.ToString()
    End Function

    ''' <summary>
    ''' Genera el correo HTML completo de seguimiento a compras para el grupo especificado.
    ''' </summary>
    Private Function GenerarHTMLSeguimientoCompras(ByVal resumen As ResumenComprasSeguimiento, ByVal tipoGrupo As String) As String
        Dim sb As New System.Text.StringBuilder()

        Dim totalSinSol As Integer = resumen.OportunidadesSinSolicitud.Count
        Dim totalEnCotiz As Integer = resumen.OportunidadesEnCotizacion.Count
        Dim todasLasOportunidades = resumen.OportunidadesSinSolicitud.Concat(resumen.OportunidadesEnCotizacion).ToList()

        Dim rojosCount As Integer = todasLasOportunidades.Where(Function(x) x.Semaforo = "ROJO").Count()
        Dim amarillosCount As Integer = todasLasOportunidades.Where(Function(x) x.Semaforo = "AMARILLO").Count()
        Dim verdesCount As Integer = todasLasOportunidades.Where(Function(x) x.Semaforo = "VERDE").Count()

        sb.AppendLine("<!DOCTYPE html>")
        sb.AppendLine("<html>")
        sb.AppendLine("<head>")
        sb.AppendLine("<meta http-equiv=""Content-Type"" content=""text/html; charset=utf-8"" />")
        sb.AppendLine("<meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />")
        sb.AppendLine("<style type=""text/css"">")
        sb.AppendLine("  body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f1f5f9; margin: 0; padding: 0; color: #1e293b; }")
        sb.AppendLine("  .wrapper-table { width: 100%; background-color: #f1f5f9; border-collapse: collapse; }")
        sb.AppendLine("  .main-card { max-width: 960px; margin: 0 auto; background-color: #ffffff; border-radius: 8px; overflow: hidden; border: 1px solid #cbd5e1; }")
        sb.AppendLine("  .main-header { background-color: #1e40af; background: linear-gradient(135deg, #1e40af 0%, #3b82f6 100%); color: #ffffff; padding: 22px 28px; text-align: left; }")
        sb.AppendLine("  .main-header h1 { margin: 0 0 6px 0; font-size: 20px; font-weight: 800; letter-spacing: -0.5px; color: #ffffff !important; }")
        sb.AppendLine("  .main-header p { margin: 0; font-size: 13px; color: #dbeafe !important; }")
        sb.AppendLine("  .kpi-banner { width: 100%; border-collapse: collapse; background-color: #f8fafc; border-bottom: 2px solid #e2e8f0; text-align: center; }")
        sb.AppendLine("  .kpi-cell { padding: 12px 10px; border-right: 1px solid #e2e8f0; }")
        sb.AppendLine("  .kpi-label { font-size: 10px; text-transform: uppercase; font-weight: 700; letter-spacing: 0.5px; color: #64748b; margin-bottom: 4px; }")
        sb.AppendLine("  .kpi-value { font-size: 18px; font-weight: 800; }")
        sb.AppendLine("  .kpi-val-tot { color: #1e293b; }")
        sb.AppendLine("  .kpi-val-grn { color: #15803d; }")
        sb.AppendLine("  .kpi-val-yel { color: #b45309; }")
        sb.AppendLine("  .kpi-val-red { color: #b91c1c; }")
        sb.AppendLine("  .kpi-val-mto { color: #0284c7; font-size: 13px; }")
        sb.AppendLine("  .container { padding: 20px 24px; background-color: #ffffff; }")
        sb.AppendLine("  .sec-heading { font-size: 15px; font-weight: 700; color: #1e3a8a; margin: 24px 0 8px 0; padding-bottom: 6px; border-bottom: 2px solid #e2e8f0; }")
        sb.AppendLine("  .sec-subtext { font-size: 12px; color: #64748b; margin: 0 0 14px 0; }")
        sb.AppendLine("  .badge-v { display: inline-block; background-color: #dcfce7; color: #15803d; border: 1px solid #86efac; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px; }")
        sb.AppendLine("  .badge-a { display: inline-block; background-color: #fef9c3; color: #a16207; border: 1px solid #fde047; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px; }")
        sb.AppendLine("  .badge-r { display: inline-block; background-color: #fee2e2; color: #b91c1c; border: 1px solid #fca5a5; padding: 2px 7px; border-radius: 10px; font-weight: 700; font-size: 11px; }")
        sb.AppendLine("  .card-prio { border-left: 5px solid #dc2626; background-color: #fff1f2; padding: 12px 16px; margin-bottom: 12px; border-radius: 4px; border-top: 1px solid #e2e8f0; border-right: 1px solid #e2e8f0; border-bottom: 1px solid #e2e8f0; }")
        sb.AppendLine("  .card-prio-meta { font-size: 11px; color: #475569; line-height: 1.5; }")
        sb.AppendLine("  .footer { background-color: #f8fafc; border-top: 1px solid #e2e8f0; padding: 16px 24px; font-size: 11px; color: #64748b; text-align: center; }")
        sb.AppendLine("</style>")
        sb.AppendLine("</head>")
        sb.AppendLine("<body style=""margin: 0; padding: 0; background-color: #f1f5f9; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; color: #1e293b;"">")
        sb.AppendLine("<table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""#f1f5f9"" class=""wrapper-table"" style=""width: 100%; border-collapse: collapse; background-color: #f1f5f9; margin: 0; padding: 0;"">")
        sb.AppendLine("  <tr>")
        sb.AppendLine("    <td align=""center"" style=""padding: 16px 8px; background-color: #f1f5f9;"">")
        sb.AppendLine("      <table role=""presentation"" align=""center"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""#ffffff"" class=""main-card"" style=""max-width: 960px; width: 100%; margin: 0 auto; background-color: #ffffff; border-radius: 8px; border: 1px solid #cbd5e1; border-collapse: separate; overflow: hidden;"">")
        sb.AppendLine("        <tr>")
        sb.AppendLine("          <td align=""left"" bgcolor=""#ffffff"" style=""background-color: #ffffff; padding: 0;"">")
        sb.AppendLine("")
        sb.AppendLine("            <!-- 1. Header principal corporativo -->")
        sb.AppendLine("            <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""#1e40af"" class=""main-header"" style=""width: 100%; border-collapse: collapse; background-color: #1e40af; background: linear-gradient(135deg, #1e40af 0%, #3b82f6 100%);"">")
        sb.AppendLine("              <tr>")
        sb.AppendLine("                <td bgcolor=""#1e40af"" style=""padding: 22px 28px; background-color: #1e40af; text-align: left;"">")
        sb.AppendLine(String.Format("                  <h1 style=""margin: 0 0 6px 0; font-size: 20px; font-weight: 800; letter-spacing: -0.5px; color: #ffffff !important;"">Informe Diario de Seguimiento a Compras - {0}</h1>", System.Net.WebUtility.HtmlEncode(tipoGrupo)))
        sb.AppendLine(String.Format("                  <p style=""margin: 0; font-size: 13px; color: #dbeafe !important;"">Oportunidades en proceso de cotización &bull; Emitido el {0:dd/MM/yyyy HH:mm:ss} &bull; Horario 08:20 AM</p>", DateTime.Now))
        sb.AppendLine("                </td>")
        sb.AppendLine("              </tr>")
        sb.AppendLine("            </table>")
        sb.AppendLine("")
        sb.AppendLine("            <!-- 2. Banner de Indicadores Clave (KPI) -->")
        sb.AppendLine("            <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" bgcolor=""#f8fafc"" class=""kpi-banner"" style=""width: 100%; border-collapse: collapse; background-color: #f8fafc; border-bottom: 2px solid #e2e8f0; text-align: center;"">")
        sb.AppendLine("              <tr>")
        sb.AppendLine(String.Format("                <td class=""kpi-cell"" bgcolor=""#f8fafc"" style=""padding: 12px 10px; border-right: 1px solid #e2e8f0; text-align: center;""><div class=""kpi-label"" style=""font-size: 10px; text-transform: uppercase; font-weight: 700; letter-spacing: 0.5px; color: #64748b; margin-bottom: 4px;"">Total Oportunidades</div><div class=""kpi-value kpi-val-tot"" style=""font-size: 18px; font-weight: 800; color: #1e293b;"">{0}</div></td>", resumen.TotalOportunidades))
        sb.AppendLine(String.Format("                <td class=""kpi-cell"" bgcolor=""#f8fafc"" style=""padding: 12px 10px; border-right: 1px solid #e2e8f0; text-align: center;""><div class=""kpi-label"" style=""font-size: 10px; text-transform: uppercase; font-weight: 700; letter-spacing: 0.5px; color: #64748b; margin-bottom: 4px;"">Sin Solicitud Prov.</div><div class=""kpi-value kpi-val-red"" style=""font-size: 18px; font-weight: 800; color: #b91c1c;"">{0}</div></td>", totalSinSol))
        sb.AppendLine(String.Format("                <td class=""kpi-cell"" bgcolor=""#f8fafc"" style=""padding: 12px 10px; border-right: 1px solid #e2e8f0; text-align: center;""><div class=""kpi-label"" style=""font-size: 10px; text-transform: uppercase; font-weight: 700; letter-spacing: 0.5px; color: #64748b; margin-bottom: 4px;"">En Cotiz. Prov.</div><div class=""kpi-value kpi-val-tot"" style=""font-size: 18px; font-weight: 800; color: #1e293b;"">{0}</div></td>", totalEnCotiz))
        sb.AppendLine(String.Format("                <td class=""kpi-cell"" bgcolor=""#f8fafc"" style=""padding: 12px 10px; border-right: 1px solid #e2e8f0; text-align: center;""><div class=""kpi-label"" style=""font-size: 10px; text-transform: uppercase; font-weight: 700; letter-spacing: 0.5px; color: #64748b; margin-bottom: 4px;"">Críticos (Rojos)</div><div class=""kpi-value kpi-val-red"" style=""font-size: 18px; font-weight: 800; color: #b91c1c;"">{0}</div></td>", rojosCount))
        sb.AppendLine(String.Format("                <td class=""kpi-cell"" bgcolor=""#f8fafc"" style=""padding: 12px 10px; border-right: 1px solid #e2e8f0; text-align: center;""><div class=""kpi-label"" style=""font-size: 10px; text-transform: uppercase; font-weight: 700; letter-spacing: 0.5px; color: #64748b; margin-bottom: 4px;"">Advertencia (Amarillos)</div><div class=""kpi-value kpi-val-yel"" style=""font-size: 18px; font-weight: 800; color: #b45309;"">{0}</div></td>", amarillosCount))
        sb.AppendLine(String.Format("                <td class=""kpi-cell"" bgcolor=""#f8fafc"" style=""padding: 12px 10px; border-right: none; text-align: center;""><div class=""kpi-label"" style=""font-size: 10px; text-transform: uppercase; font-weight: 700; letter-spacing: 0.5px; color: #64748b; margin-bottom: 4px;"">En Tiempo (Verdes)</div><div class=""kpi-value kpi-val-grn"" style=""font-size: 18px; font-weight: 800; color: #15803d;"">{0}</div></td>", verdesCount))
        sb.AppendLine("              </tr>")
        sb.AppendLine("            </table>")
        sb.AppendLine("")
        sb.AppendLine("            <div class=""container"" style=""padding: 20px 24px; background-color: #ffffff;"">")
        sb.AppendLine("")
        sb.AppendLine("              <!-- ========================================================================= -->")
        sb.AppendLine("              <!-- 1. OPORTUNIDADES SIN SOLICITUD DE COTIZACIÓN A PROVEEDOR                     -->")
        sb.AppendLine("              <!-- ========================================================================= -->")
        sb.AppendLine(String.Format("              <div class=""sec-heading"" style=""font-size: 15px; font-weight: 700; color: #1e3a8a; margin: 12px 0 6px 0; padding-bottom: 6px; border-bottom: 2px solid #e2e8f0;"">&#9888; 1. Oportunidades Sin Solicitud de Cotización a Proveedor ({0})</div>", totalSinSol))
        sb.AppendLine("              <p class=""sec-subtext"" style=""font-size: 12px; color: #64748b; margin: 0 0 14px 0;"">Oportunidades de venta enviadas por ventas que aún no cuentan con ninguna solicitud de cotización emitida a proveedores en el sistema. Demandan asignación de proveedor y emisión urgente.</p>")
        sb.AppendLine("")

        If totalSinSol = 0 Then
            sb.AppendLine("              <div style=""font-size: 12px; color: #166534; background-color: #dcfce7; border: 1px solid #86efac; padding: 12px; border-radius: 6px; margin-bottom: 18px;"">&#10004; Excelente: Todas las oportunidades de este grupo cuentan con solicitud de cotización a proveedor.</div>")
        Else
            For Each itm In resumen.OportunidadesSinSolicitud
                sb.Append(GenerarTarjetaSeguimientoComprasHtml(itm))
            Next
        End If

        sb.AppendLine("")
        sb.AppendLine("              <!-- ========================================================================= -->")
        sb.AppendLine("              <!-- 2. OPORTUNIDADES EN PROCESO DE COTIZACIÓN CON PROVEEDORES                   -->")
        sb.AppendLine("              <!-- ========================================================================= -->")
        sb.AppendLine(String.Format("              <div class=""sec-heading"" style=""font-size: 15px; font-weight: 700; color: #1e3a8a; margin: 24px 0 6px 0; padding-bottom: 6px; border-bottom: 2px solid #e2e8f0;"">&#9203; 2. Oportunidades en Proceso de Cotización con Proveedores ({0})</div>", totalEnCotiz))
        sb.AppendLine("              <p class=""sec-subtext"" style=""font-size: 12px; color: #64748b; margin: 0 0 14px 0;"">Oportunidades con solicitudes emitidas a proveedores pero pendientes de recibir costos o completar la cotización interna correspondiente.</p>")
        sb.AppendLine("")

        If totalEnCotiz = 0 Then
            sb.AppendLine("              <div style=""font-size: 12px; color: #166534; background-color: #dcfce7; border: 1px solid #86efac; padding: 12px; border-radius: 6px; margin-bottom: 18px;"">&#10004; No hay solicitudes en espera de cotización con proveedores en este momento.</div>")
        Else
            For Each itm In resumen.OportunidadesEnCotizacion
                sb.Append(GenerarTarjetaSeguimientoComprasHtml(itm))
            Next
        End If

        sb.AppendLine("            </div>")
        sb.AppendLine("")
        sb.AppendLine("            <!-- Footer institucional -->")
        sb.AppendLine("            <div class=""footer"" style=""background-color: #f8fafc; border-top: 1px solid #e2e8f0; padding: 16px 24px; font-size: 11px; color: #64748b; text-align: center;"">")
        sb.AppendLine("              HistoMedic Robot LFM IA &bull; Rutina automática matutina de seguimiento a compras (08:20 AM Lunes a Viernes).<br/>")
        sb.AppendLine("              Por favor no responda directamente a este correo automático; para cualquier actualización registre el seguimiento en el sistema.")
        sb.AppendLine("            </div>")
        sb.AppendLine("")
        sb.AppendLine("          </td>")
        sb.AppendLine("        </tr>")
        sb.AppendLine("      </table>")
        sb.AppendLine("    </td>")
        sb.AppendLine("  </tr>")
        sb.AppendLine("</table>")
        sb.AppendLine("</body>")
        sb.AppendLine("</html>")

        Return sb.ToString()
    End Function

#End Region

#Region "Sincronización Pases de Salida Servidor Central -> Local"

    ''' <summary>
    ''' Sincroniza y descarga desde el servidor central hacia la base de datos local
    ''' los registros de tb_pases_salida cuyo campo sinc = 1.
    ''' Actualiza exclusivamente los 6 campos:
    ''' sinc, firma_recibe, fch_usuario_recibe, nombre_recibio_salida, ccveusuario_recibe, enviado.
    ''' </summary>
    Public Sub DescargarPasesSalidaCentral()

        If Not _servidorCentralConectado Then
            Exit Sub
        End If

        SyncLock _lockPasesSalida
            If _sincronizandoPasesSalida Then
                Exit Sub
            End If
            _sincronizandoPasesSalida = True
        End SyncLock

        Try
            ' 1. Asegurar conexiones abiertas
            If cx_MySQL_CentralAsync.State = ConnectionState.Closed Then
                cx_MySQL_CentralAsync.Open()
            End If
            If cx_MySQL_localAsync.State = ConnectionState.Closed Then
                cx_MySQL_localAsync.Open()
            End If

            ' 2. Consultar registros en servidor central con sinc = 1
            Dim sqlCentral As String = "SELECT id, sinc, firma_recibe, fch_usuario_recibe, nombre_recibio_salida, ccveusuario_recibe, enviado " & _
                                       "FROM tb_pases_salida WHERE sinc = 1;"

            Dim dtCentral As New DataTable()
            Using cmdCentral As New MySqlConnector.MySqlCommand(sqlCentral, cx_MySQL_CentralAsync)
                cmdCentral.CommandTimeout = 60
                Using daCentral As New MySqlConnector.MySqlDataAdapter(cmdCentral)
                    daCentral.Fill(dtCentral)
                End Using
            End Using

            If dtCentral Is Nothing OrElse dtCentral.Rows.Count = 0 Then
                Exit Sub
            End If

            ' Control de duplicados dentro del lote
            Dim idsProcesados As New HashSet(Of Integer)()

            Dim sqlCheckLocal As String = "SELECT COUNT(1) FROM tb_pases_salida WHERE id = @id;"
            Dim sqlUpdateLocal As String = "UPDATE tb_pases_salida SET " & _
                                           "sinc = @sinc, " & _
                                           "firma_recibe = @firma_recibe, " & _
                                           "fch_usuario_recibe = @fch_usuario_recibe, " & _
                                           "nombre_recibio_salida = @nombre_recibio_salida, " & _
                                           "ccveusuario_recibe = @ccveusuario_recibe, " & _
                                           "enviado = @enviado " & _
                                           "WHERE id = @id;"

            Dim sqlAckCentral As String = "UPDATE tb_pases_salida SET sinc = 0 WHERE id = @id;"

            For Each rowCentral As DataRow In dtCentral.Rows

                Dim idCentral As Integer = 0
                If IsDBNull(rowCentral("id")) OrElse Not Integer.TryParse(rowCentral("id").ToString(), idCentral) OrElse idCentral <= 0 Then
                    Continue For
                End If

                ' Evitar procesar duplicados
                If idsProcesados.Contains(idCentral) Then
                    Continue For
                End If
                idsProcesados.Add(idCentral)

                ' Procesar de forma segura cada registro individualmente
                Try
                    ' Verificar si existe localmente
                    Dim existeLocal As Boolean = False
                    Using cmdCheck As New MySqlConnector.MySqlCommand(sqlCheckLocal, cx_MySQL_localAsync)
                        cmdCheck.Parameters.Add("@id", MySqlConnector.MySqlDbType.Int32).Value = idCentral
                        Dim countObj As Object = cmdCheck.ExecuteScalar()
                        If countObj IsNot Nothing AndAlso Not IsDBNull(countObj) AndAlso Convert.ToInt32(countObj) > 0 Then
                            existeLocal = True
                        End If
                    End Using

                    ' Si no existe localmente, no crear registros ni alterar otras tablas
                    If Not existeLocal Then
                        LogEventos.Escribir(String.Format("[Pases Salida] El registro ID {0} no existe en la base de datos local. Se omite actualización.", idCentral))
                        Continue For
                    End If

                    ' Actualizar exclusivamente los 6 campos en local usando parámetros SQL y transacción
                    Dim actualizadoLocal As Boolean = False
                    Using trLocal As MySqlConnector.MySqlTransaction = cx_MySQL_localAsync.BeginTransaction()
                        Try
                            Using cmdUpdate As New MySqlConnector.MySqlCommand(sqlUpdateLocal, cx_MySQL_localAsync, trLocal)
                                cmdUpdate.Parameters.Add("@id", MySqlConnector.MySqlDbType.Int32).Value = idCentral

                                ' 1. sinc
                                Dim valSinc As Integer = 1
                                If Not IsDBNull(rowCentral("sinc")) AndAlso Integer.TryParse(rowCentral("sinc").ToString(), valSinc) Then
                                    cmdUpdate.Parameters.Add("@sinc", MySqlConnector.MySqlDbType.Int32).Value = valSinc
                                Else
                                    cmdUpdate.Parameters.Add("@sinc", MySqlConnector.MySqlDbType.Int32).Value = 1
                                End If

                                ' 2. firma_recibe
                                If IsDBNull(rowCentral("firma_recibe")) OrElse String.IsNullOrWhiteSpace(rowCentral("firma_recibe").ToString()) Then
                                    cmdUpdate.Parameters.Add("@firma_recibe", MySqlConnector.MySqlDbType.LongText).Value = DBNull.Value
                                Else
                                    cmdUpdate.Parameters.Add("@firma_recibe", MySqlConnector.MySqlDbType.LongText).Value = rowCentral("firma_recibe").ToString().Trim()
                                End If

                                ' 3. fch_usuario_recibe
                                If IsDBNull(rowCentral("fch_usuario_recibe")) OrElse String.IsNullOrWhiteSpace(rowCentral("fch_usuario_recibe").ToString()) Then
                                    cmdUpdate.Parameters.Add("@fch_usuario_recibe", MySqlConnector.MySqlDbType.DateTime).Value = DBNull.Value
                                Else
                                    Dim fchRecibe As DateTime
                                    If DateTime.TryParse(rowCentral("fch_usuario_recibe").ToString(), fchRecibe) Then
                                        cmdUpdate.Parameters.Add("@fch_usuario_recibe", MySqlConnector.MySqlDbType.DateTime).Value = fchRecibe
                                    Else
                                        cmdUpdate.Parameters.Add("@fch_usuario_recibe", MySqlConnector.MySqlDbType.DateTime).Value = DBNull.Value
                                    End If
                                End If

                                ' 4. nombre_recibio_salida
                                If IsDBNull(rowCentral("nombre_recibio_salida")) OrElse String.IsNullOrWhiteSpace(rowCentral("nombre_recibio_salida").ToString()) Then
                                    cmdUpdate.Parameters.Add("@nombre_recibio_salida", MySqlConnector.MySqlDbType.VarChar, 255).Value = DBNull.Value
                                Else
                                    cmdUpdate.Parameters.Add("@nombre_recibio_salida", MySqlConnector.MySqlDbType.VarChar, 255).Value = rowCentral("nombre_recibio_salida").ToString().Trim()
                                End If

                                ' 5. ccveusuario_recibe
                                If IsDBNull(rowCentral("ccveusuario_recibe")) OrElse String.IsNullOrWhiteSpace(rowCentral("ccveusuario_recibe").ToString()) Then
                                    cmdUpdate.Parameters.Add("@ccveusuario_recibe", MySqlConnector.MySqlDbType.VarChar, 45).Value = DBNull.Value
                                Else
                                    cmdUpdate.Parameters.Add("@ccveusuario_recibe", MySqlConnector.MySqlDbType.VarChar, 45).Value = rowCentral("ccveusuario_recibe").ToString().Trim()
                                End If

                                ' 6. enviado
                                Dim valEnviado As Integer = 0
                                If Not IsDBNull(rowCentral("enviado")) AndAlso Integer.TryParse(rowCentral("enviado").ToString(), valEnviado) Then
                                    cmdUpdate.Parameters.Add("@enviado", MySqlConnector.MySqlDbType.Int32).Value = valEnviado
                                Else
                                    cmdUpdate.Parameters.Add("@enviado", MySqlConnector.MySqlDbType.Int32).Value = 0
                                End If

                                cmdUpdate.ExecuteNonQuery()
                            End Using

                            trLocal.Commit()
                            actualizadoLocal = True
                        Catch exUpdateLocal As Exception
                            Try
                                trLocal.Rollback()
                            Catch exRb As Exception
                            End Try
                            LogEventos.Escribir(String.Format("[Pases Salida] Error al actualizar localmente pase ID {0}: {1}", idCentral, exUpdateLocal.Message))
                        End Try
                    End Using

                    ' Si se actualizó localmente de forma exitosa, confirmar en el servidor central actualizando sinc = 0
                    If actualizadoLocal Then
                        Try
                            Using cmdAck As New MySqlConnector.MySqlCommand(sqlAckCentral, cx_MySQL_CentralAsync)
                                cmdAck.Parameters.Add("@id", MySqlConnector.MySqlDbType.Int32).Value = idCentral
                                cmdAck.ExecuteNonQuery()
                            End Using

                            LogEventos.Escribir(String.Format("[Pases Salida] Pase de salida ID {0} sincronizado exitosamente desde el servidor central.", idCentral))
                        Catch exAck As Exception
                            LogEventos.Escribir(String.Format("[Pases Salida] Registro ID {0} actualizado localmente pero error al confirmar en central: {1}", idCentral, exAck.Message))
                        End Try
                    End If

                Catch exRegistro As Exception
                    ' Continuar con el siguiente registro en caso de fallo individual
                    LogEventos.Escribir(String.Format("[Pases Salida] Error en proceso individual de pase ID {0}: {1}", idCentral, exRegistro.Message))
                End Try

            Next

        Catch exMySql As MySqlConnector.MySqlException
            LogEventos.Escribir(String.Format("[Pases Salida] Error MySQL en sincronización: {0}", exMySql.Message))
            NotificarDesconexionCentral("Error de conexión al sincronizar pases de salida: " & exMySql.Message)
        Catch exGeneral As Exception
            LogEventos.Escribir(String.Format("[Pases Salida] Error general en DescargarPasesSalidaCentral: {0}", exGeneral.Message))
        Finally
            SyncLock _lockPasesSalida
                _sincronizandoPasesSalida = False
            End SyncLock
        End Try

    End Sub

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
