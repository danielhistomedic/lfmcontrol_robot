Imports System.IO
Imports System.Net
Imports System.Net.Security
Imports System.Security.Cryptography.X509Certificates

Public Class FTPHosting

    Private Const FTP_HOST As String = "ftp.lfmcontrol.com.mx"
    Private Const FTP_USUARIO As String = "lfmcontr"
    Private Const FTP_PASSWORD As String = "JdM:3H2n-j4O1o"

    Public Function SubirArchivo(rutaLocal As String,
                                 nombreArchivoRemoto As String, FTP_CARPETA As String) As Boolean

        Try
            ' 1. Normalizar carpeta remota
            Dim carpetaRemota As String = FTP_CARPETA
            If String.IsNullOrWhiteSpace(carpetaRemota) Then
                carpetaRemota = "/"
            Else
                If Not carpetaRemota.StartsWith("/") Then carpetaRemota = "/" & carpetaRemota
                If Not carpetaRemota.EndsWith("/") Then carpetaRemota &= "/"
            End If

            ' 2. Asegurar que el directorio de destino existe en el servidor FTP
            CrearDirectorioRemotoSiNoExiste(carpetaRemota)

            Dim ftpUrl As String = "ftp://" & FTP_HOST & carpetaRemota & nombreArchivoRemoto
            Dim fileBytes As Byte() = System.IO.File.ReadAllBytes(rutaLocal)
            Dim uploaded As Boolean = False
            Dim lastError As String = ""

            ' Intento 1: Modo Pasivo (UsePassive = True), KeepAlive = False
            Try
                Dim request As System.Net.FtpWebRequest = CType(System.Net.WebRequest.Create(ftpUrl), System.Net.FtpWebRequest)
                request.Method = System.Net.WebRequestMethods.Ftp.UploadFile
                request.Credentials = New System.Net.NetworkCredential(FTP_USUARIO, FTP_PASSWORD)
                request.UseBinary = True
                request.UsePassive = True
                request.KeepAlive = False
                request.ContentLength = fileBytes.Length

                Using requestStream As System.IO.Stream = request.GetRequestStream()
                    requestStream.Write(fileBytes, 0, fileBytes.Length)
                End Using

                Using response As System.Net.FtpWebResponse = CType(request.GetResponse(), System.Net.FtpWebResponse)
                    uploaded = True
                End Using
            Catch ex As Exception
                lastError = ex.Message
            End Try

            ' Intento 2 (Fallback): Modo Activo (UsePassive = False), KeepAlive = False
            If Not uploaded Then
                Try
                    Dim request As System.Net.FtpWebRequest = CType(System.Net.WebRequest.Create(ftpUrl), System.Net.FtpWebRequest)
                    request.Method = System.Net.WebRequestMethods.Ftp.UploadFile
                    request.Credentials = New System.Net.NetworkCredential(FTP_USUARIO, FTP_PASSWORD)
                    request.UseBinary = True
                    request.UsePassive = False
                    request.KeepAlive = False
                    request.ContentLength = fileBytes.Length

                    Using requestStream As System.IO.Stream = request.GetRequestStream()
                        requestStream.Write(fileBytes, 0, fileBytes.Length)
                    End Using

                    Using response As System.Net.FtpWebResponse = CType(request.GetResponse(), System.Net.FtpWebResponse)
                        uploaded = True
                    End Using
                Catch ex As Exception
                    lastError = ex.Message
                End Try
            End If

            If Not uploaded Then
                LogEventos.Escribir("Error al subir archivo a hosting FTP (" & ftpUrl & "): " & lastError)
                Return False
            End If

            ' 4. Eliminar el archivo temporal
            Try
                System.IO.File.Delete(rutaLocal)
            Catch ex As Exception
                LogEventos.Escribir("Error al eliminar archivo temporal (" & rutaLocal & "): " & ex.Message)
            End Try

            Return True

        Catch ex As Exception
            LogEventos.Escribir("Error en FTPHosting.SubirArchivo: " & ex.ToString())
        End Try

        Return False

    End Function

    Private Function ExisteDirectorioRemoto(rutaDirectorio As String) As Boolean
        Try
            Dim request As System.Net.FtpWebRequest = CType(System.Net.WebRequest.Create(rutaDirectorio & "/"), System.Net.FtpWebRequest)
            request.Credentials = New System.Net.NetworkCredential(FTP_USUARIO, FTP_PASSWORD)
            request.Method = System.Net.WebRequestMethods.Ftp.ListDirectory
            request.UsePassive = True
            request.KeepAlive = False

            Using response As System.Net.FtpWebResponse = CType(request.GetResponse(), System.Net.FtpWebResponse)
                Return True
            End Using
        Catch ex As Exception
            Return False
        End Try
    End Function

    Private Sub CrearDirectorioRemotoSiNoExiste(carpetaRemota As String)
        Try
            If String.IsNullOrWhiteSpace(carpetaRemota) Then Return

            Dim partes() As String = carpetaRemota.Split(New Char() {"/"c}, StringSplitOptions.RemoveEmptyEntries)
            Dim rutaAcumulada As String = "ftp://" & FTP_HOST

            For Each parte As String In partes
                If String.IsNullOrWhiteSpace(parte) Then Continue For
                rutaAcumulada &= "/" & parte

                ' Solo intentar crear la carpeta si aún no existe
                If Not ExisteDirectorioRemoto(rutaAcumulada) Then
                    Try
                        Dim req As System.Net.FtpWebRequest = CType(System.Net.WebRequest.Create(rutaAcumulada), System.Net.FtpWebRequest)
                        req.Credentials = New System.Net.NetworkCredential(FTP_USUARIO, FTP_PASSWORD)
                        req.Method = System.Net.WebRequestMethods.Ftp.MakeDirectory
                        req.UsePassive = True
                        req.KeepAlive = False

                        Using resp As System.Net.FtpWebResponse = CType(req.GetResponse(), System.Net.FtpWebResponse)
                        End Using
                    Catch ex As System.Net.WebException
                        ' Si el servidor devuelve 550 (ya existe o no disponible), ignorarlo silenciosamente
                        Dim webResp As System.Net.FtpWebResponse = CType(ex.Response, System.Net.FtpWebResponse)
                        If webResp Is Nothing OrElse webResp.StatusCode <> System.Net.FtpStatusCode.ActionNotTakenFileUnavailable Then
                            LogEventos.Escribir("Aviso al crear carpeta FTP (" & rutaAcumulada & "): " & ex.Message)
                        End If
                    Catch ex As Exception
                        ' Ignorar otros fallos no críticos
                    End Try
                End If
            Next
        Catch ex As Exception
            LogEventos.Escribir("Error en CrearDirectorioRemotoSiNoExiste: " & ex.Message)
        End Try
    End Sub

End Class
