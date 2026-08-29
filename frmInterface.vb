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

        If valor Then
            Me.btnConectarDBCentral.TextColor = Color.Green
        Else
            Me.btnConectarDBCentral.TextColor = Color.Crimson
        End If

    End Function

    Private Function HabilitarEstatusConexionLocal(ByVal valor As Boolean)

        If valor Then
            Me.btnConectarLocal.TextColor = Color.Green
        Else
            Me.btnConectarLocal.TextColor = Color.Crimson
        End If

    End Function

    Private Function Conectar_Central(clues) As Boolean

        Try

            Dim Database As String = ""
            Dim Uid As String = ""
            Dim Pwd As String = ""

            'const DB_NAME = "lfmcontr_sistema"; //Producción
            'const DB_PORT = "3306"; //Producción
            'const DB_USER = "lfmcontr_admin"; //Producción
            'const DB_PASSWORD = "fkSp_EkB6dX_"; //Producción


            ' ============================================================= =====================================================================
            Dim cadena_conexion_admin As String = "Server=histomedic.mx;Database=mirtheda_admin;Uid=mirtheda_root;Pwd=Bsapmd2cKb*5;SSL Mode=None;"
            If cx_MySQL_Admin.State = ConnectionState.Closed Then
                If Not Test_MySQL_Admin(cadena_conexion_admin) Then
                    Return False
                End If
            End If

            tb_ClienteData = tb_Recordset_MySQL_Admin("SELECT * FROM ssf_clientes WHERE clues = '" & clues & "'")
            If tb_ClienteData.Rows.Count = 0 Then
                AgregarLog(500, ".Error de Conexión con el Servidor. ")
                Return False
            End If


            Database = tb_ClienteData.Rows(0).Item("db_name").ToString
            Uid = tb_ClienteData.Rows(0).Item("db_user").ToString
            Pwd = tb_ClienteData.Rows(0).Item("db_pass").ToString

            If tb_ClienteData.Rows(0).Item("actualizaciones").ToString <> "SI" Then
                AgregarLog(500, "No Disponible para actualizaciones. ")
                Return False
            End If

            ' ==================================================================================================================================

            If cx_MySQL_Central.State = ConnectionState.Open Then
                Me.HabilitarEstatusConexionCentral(True)
                Return True
            End If

            Dim cadena_conexion As String = "Server=lfmcontrol.com.mx;Database=" & Database & ";Uid=" & Uid & ";Pwd=" & Pwd & ";SSL Mode=None;"
            If cx_MySQL_Central.State = ConnectionState.Closed Then
                If Test_MySQL_Central(cadena_conexion) Then
                    Me.HabilitarEstatusConexionCentral(True)
                    Test_MySQL_CentralAsync(cadena_conexion)
                    Test_MySQL_CentralAsyncALM(cadena_conexion)
                    Return True
                Else
                    Return False
                End If
            End If

        Catch ex As Exception

            AgregarLog(500, ex.Message & ". Equipo No Compatible con WebRequest ")
            'Me.lstLog.Items.Add(Calcula_FechaActual.ToString)
            'Me.lstLog.Items(lstLog.Items.Count - 1).SubItems.Add(ex.Message & ". Equipo No Compatible con WebRequest ")
            Return False
        End Try

        Return True

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
            ' Rutinas Importar 
            '======================================

            '======================================
            ' Rutinas Exportar 
            '======================================

            '== Exportar Archivso Adjuntos 
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

            ' ======================================

            'Me.ProgressBarX1.Value = 0

            '== Reinicia la aplicacion si el log, tiene mas de 3 registros.  == == == == 
            Try
                If lstLog.Items.Count > 5 Then
                    Me.lstLog.Items.Clear()
                    Me.DetenerProcesoSP()
                    TimerEnlace.Enabled = False
                    Application.Restart()
                End If
            Catch ex As Exception
                LogEventos.Escribir("Reinicia. " & ex.Message)
            End Try

            ' == == == == == == == == == == == == == == == == == == == == == == == == ==

            ' == Cotinuamente Verificando si la task ya fue terminada
            ReiniciarProcesoSP()
            ' == == == == == == == == == == == == == == == =

            TimerEnlace.Enabled = True

        Catch ex As Exception
            TimerEnlace.Enabled = True
            LogEventos.Escribir("Error General. " & ex.Message)
        End Try

    End Sub

    Private Sub ExportarDataToHostingSP_Load(token As CancellationToken)

        While Not token.IsCancellationRequested
            Me.ExportarDataToHostingSP()
            Me.ExportarDataToHostingSP_Almacen()
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
            ciclo = ciclo + 1
            If ciclo = 3 Then
                AgregarLog(500, ex.Message & ", Error al actualizar registro central AsyncALM: " & Cadena)
                Exit Try
            End If
            GoTo intenta_otravz
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
                chkActivar.Checked = False
                chkActivar.Enabled = False
            End If

        Else

            Application.ExitThread()

        End If

        load_init = False

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
