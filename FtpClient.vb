Imports System.Net
Imports System.IO
Imports System.Threading

Public Class FtpClient

    Private ReadOnly _host As String
    Private ReadOnly _user As String
    Private ReadOnly _pass As String
    Private ReadOnly _useSsl As Boolean

    ' 🔥 Configuración de reintentos
    Public Property MaxReintentos As Integer = 3
    Public Property DelayBaseMs As Integer = 1000 ' 1 segundo
    Public Property UsarBackoff As Boolean = True

    Public Sub New(host As String, user As String, pass As String, Optional useSsl As Boolean = False)

        Dim cleanHost As String = If(host, "").Trim()
        If cleanHost.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) Then
            cleanHost = cleanHost.Substring(6)
        ElseIf cleanHost.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase) Then
            cleanHost = cleanHost.Substring(7)
        ElseIf cleanHost.StartsWith("http://", StringComparison.OrdinalIgnoreCase) Then
            cleanHost = cleanHost.Substring(7)
        End If
        cleanHost = cleanHost.Trim("/"c, " "c)

        If String.IsNullOrWhiteSpace(cleanHost) Then
            cleanHost = "127.0.0.1"
        End If

        _host = cleanHost
        _user = user
        _pass = pass
        _useSsl = useSsl

    End Sub

    Private Function ConstruirUrlRemota(rutaRemota As String) As String
        If String.IsNullOrWhiteSpace(rutaRemota) Then
            Throw New ArgumentException("La ruta remota FTP no puede estar vacía.")
        End If

        Dim rutaLimpia As String = rutaRemota.Trim().Replace("\"c, "/"c)
        Dim urlFinal As String = ""

        If rutaLimpia.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) OrElse rutaLimpia.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase) Then
            urlFinal = rutaLimpia
        Else
            If Not rutaLimpia.StartsWith("/") Then
                rutaLimpia = "/" & rutaLimpia
            End If
            urlFinal = "ftp://" & _host & rutaLimpia
        End If

        Dim uriResult As Uri = Nothing
        If Not Uri.TryCreate(urlFinal, UriKind.Absolute, uriResult) OrElse String.IsNullOrWhiteSpace(uriResult.Host) Then
            Throw New UriFormatException("URI no válido: no se pudo analizar la autoridad ni el host en '" & urlFinal & "'")
        End If

        Return uriResult.AbsoluteUri
    End Function

    Private Function CrearRequest(rutaRemota As String, metodo As String) As FtpWebRequest

        Dim urlValida As String = ConstruirUrlRemota(rutaRemota)
        Dim request As FtpWebRequest = CType(WebRequest.Create(urlValida), FtpWebRequest)
        request.Method = metodo
        request.Credentials = New NetworkCredential(_user, _pass)
        request.UseBinary = True
        request.KeepAlive = False
        request.EnableSsl = _useSsl
        request.Timeout = 15000
        request.ReadWriteTimeout = 15000
        Return request

    End Function

    ' 🔥 Núcleo de reintentos
    Private Function EjecutarConReintento(Of T)(func As Func(Of T)) As T
        Dim intento As Integer = 0

        While True
            Try
                Return func()
            Catch ex As UriFormatException
                LogEventos.Escribir("FtpClient error de URI no válido: " & ex.Message)
                Exit While
            Catch ex As ArgumentException
                LogEventos.Escribir("FtpClient error de parámetro: " & ex.Message)
                Exit While
            Catch ex As Exception
                intento += 1

                If intento >= MaxReintentos Then
                    LogEventos.Escribir("FtpClient error tras " & MaxReintentos & " intentos: " & ex.Message)
                    Exit While
                End If

                ' Calcular delay
                Dim delay = DelayBaseMs
                If UsarBackoff Then
                    delay = DelayBaseMs * Math.Pow(2, intento - 1) ' 1s, 2s, 4s...
                End If

                Thread.Sleep(delay)
            End Try
        End While

        Return CType(Nothing, T)
    End Function

    ' 🔹 Subir archivo con reintento
    Public Function SubirArchivo(rutaLocal As String, rutaRemota As String) As Boolean
        Return EjecutarConReintento(Function()

                                        Dim request = CrearRequest(rutaRemota, WebRequestMethods.Ftp.UploadFile)
                                        Dim bytes = File.ReadAllBytes(rutaLocal)
                                        request.ContentLength = bytes.Length

                                        Using stream = request.GetRequestStream()
                                            stream.Write(bytes, 0, bytes.Length)
                                        End Using

                                        Using response = CType(request.GetResponse(), FtpWebResponse)
                                            Return True
                                        End Using

                                    End Function)
    End Function

    ' 🔹 Descargar archivo con reintento
    Public Function DescargarArchivo(rutaRemota As String, rutaLocal As String) As Boolean

        Return EjecutarConReintento(Function()

                                        Dim request = CrearRequest(rutaRemota, WebRequestMethods.Ftp.DownloadFile)

                                        Using response = CType(request.GetResponse(), FtpWebResponse)
                                            Using stream = response.GetResponseStream()
                                                Using fs As New FileStream(rutaLocal, FileMode.Create)
                                                    stream.CopyTo(fs)
                                                End Using
                                            End Using
                                        End Using

                                        Return True

                                    End Function)
    End Function

    ' 🔹 Eliminar archivo con reintento
    Public Function EliminarArchivo(rutaRemota As String) As Boolean
        Return EjecutarConReintento(Function()

                                        Dim request = CrearRequest(rutaRemota, WebRequestMethods.Ftp.DeleteFile)

                                        Using response = CType(request.GetResponse(), FtpWebResponse)
                                            Return True
                                        End Using

                                    End Function)
    End Function

End Class
