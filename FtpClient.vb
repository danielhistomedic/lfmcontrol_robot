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

        _host = host.TrimEnd("/"c)
        _user = user
        _pass = pass
        _useSsl = useSsl

    End Sub

    Private Function CrearRequest(ruta As String, metodo As String) As FtpWebRequest

        Dim request As FtpWebRequest = CType(WebRequest.Create(ruta), FtpWebRequest)
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
