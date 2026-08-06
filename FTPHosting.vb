Imports System.IO
Imports System.Net
Imports System.Net.Security
Imports System.Security.Cryptography.X509Certificates

Public Class FTPHosting

    Private Const FTP_HOST As String = "lfmcontrol.com.mx"
    Private Const FTP_USUARIO As String = "lfmcontr"
    Private Const FTP_PASSWORD As String = "JdM:3H2n-j4O1o"

    Public Function SubirArchivo(rutaLocal As String,
                                 nombreArchivoRemoto As String, FTP_CARPETA As String) As Boolean

        Try

            ' 3. Subir el archivo por medio de FTP a la carpeta del portal usando FtpWebRequest (KeepAlive=False y soporte pasivo/activo)
            ' Datos ftp: usuario: ftp_admin@histomedic.mx, password: Jhytmd2cKb*5, host: histomedic.mx
            ' Ruta: /portal.histomedic.mx/Assets/files/productos
            Dim ftpUrl As String = "ftp://" & FTP_HOST & FTP_CARPETA & nombreArchivoRemoto
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
                Throw New Exception("No se pudo subir el archivo por FTP. Detalle: " & lastError)
                Return False
            End If

            ' 4. Eliminar el archivo temporal
            Try
                System.IO.File.Delete(rutaLocal)
            Catch ex As Exception
                LogEventos.Escribir(ex.ToString())
            End Try

            'Funciones.Msj_Info("Imagen registrada exitosamente")

            Return True

        Catch ex As Exception
            LogEventos.Escribir(ex.ToString())
        End Try

        Return False

    End Function

End Class
