Imports System.Net
Imports System.IO
Imports System.Text
Imports System.Collections.Generic
Imports Newtonsoft.Json

Public Class whatsapp

    Shared Function EnviarMensajeWhatsAppAPI(params As Parameters) As Boolean

        ' URL de facebook para usar las plantillas 
        Dim url As String = String.Format("https://graph.facebook.com/v22.0/{0}/messages", params.PhoneNumberID)

        ' Crear cuerpo de la peticion POST (En esta funcion se llenan las variables del cuerpo de la plantilla de WhatsApp) 
        Dim requestData As String = BuildJsonRequest(params)

        ' Consumir la API (Hacer peticion POST) ya con los datos cargados (Toda esta parte de abajo es fija, no necesita modificar nada aqui)
        'Console.WriteLine(vbCrLf & "URL de destino:")
        'Console.WriteLine(url)

        'Console.WriteLine(vbCrLf & "JSON a enviar:")
        'Console.WriteLine(requestData)

        Try
            Dim request As HttpWebRequest = DirectCast(WebRequest.Create(url), HttpWebRequest)
            request.Method = "POST"
            request.Headers.Add("Authorization", "Bearer " & params.AccessToken)
            'request.ContentType = "application/json; charset=UTF-8"
            request.ContentType = "application/json"

            ' Escribir los datos
            Dim byteArray As Byte() = Encoding.UTF8.GetBytes(requestData)
            request.ContentLength = byteArray.Length

            Using dataStream As Stream = request.GetRequestStream()
                dataStream.Write(byteArray, 0, byteArray.Length)
            End Using

            ' Obtener respuesta
            Using response As HttpWebResponse = DirectCast(request.GetResponse(), HttpWebResponse)
            End Using

            Return True
            'Console.WriteLine(vbCrLf & "¡Mensaje enviado con éxito!")

        Catch ex As WebException

            'Console.WriteLine(vbCrLf & "ERROR en la petición:")
            If ex.Response IsNot Nothing Then
                Using reader As New StreamReader(ex.Response.GetResponseStream())
                    'Console.WriteLine(reader.ReadToEnd())
                End Using
            Else
                'Console.WriteLine(ex.Message)
            End If
            Return False
        Catch ex As Exception

            'Console.WriteLine(vbCrLf & "ERROR general:")
            'Console.WriteLine(ex.Message)
            Return False
        End Try

        Return False

    End Function
    Shared Function BuildJsonRequest(params As Parameters) As String

        If params Is Nothing Then Return ""

        Dim components As New List(Of Object)()

        ' Componentes comunes de cabecera de imagen
        If params.TemplateName = "confrimacion_cita" OrElse _
           params.TemplateName = "orden_internamiento_tratamiento" OrElse _
           params.TemplateName = "ingresos_clinica" Then

            Dim headerParam As New Dictionary(Of String, Object) From {
                {"type", "image"},
                {"image", New Dictionary(Of String, Object) From {
                    {"link", params.HeaderImageURL}
                }}
            }
            components.Add(New Dictionary(Of String, Object) From {
                {"type", "header"},
                {"parameters", New Object() {headerParam}}
            })
        End If

        ' Parámetros del cuerpo (body) según plantilla
        Dim bodyParams As New List(Of Object)()

        Select Case params.TemplateName
            Case "confrimacion_cita"
                bodyParams.Add(New Dictionary(Of String, String) From {{"type", "text"}, {"text", params.UserName}})
                bodyParams.Add(New Dictionary(Of String, String) From {{"type", "text"}, {"text", params.MedicalUnit}})
                bodyParams.Add(New Dictionary(Of String, String) From {{"type", "text"}, {"text", params.FolioCitaOI}})
                bodyParams.Add(New Dictionary(Of String, String) From {{"type", "text"}, {"text", params.AppointmentDate}})
                bodyParams.Add(New Dictionary(Of String, String) From {{"type", "text"}, {"text", params.AppointmentHour}})
                bodyParams.Add(New Dictionary(Of String, String) From {{"type", "text"}, {"text", params.DoctorName}})
                bodyParams.Add(New Dictionary(Of String, String) From {{"type", "text"}, {"text", params.Office}})
                bodyParams.Add(New Dictionary(Of String, String) From {{"type", "text"}, {"text", params.MedicalPhone}})
                bodyParams.Add(New Dictionary(Of String, String) From {{"type", "text"}, {"text", params.TiempoPresentarseAntesIngreso}})

            Case "cancelacion_cita"
                bodyParams.Add(New Dictionary(Of String, String) From {{"type", "text"}, {"text", params.UserName}})
                bodyParams.Add(New Dictionary(Of String, String) From {{"type", "text"}, {"text", params.MedicalUnit}})
                bodyParams.Add(New Dictionary(Of String, String) From {{"type", "text"}, {"text", params.AppointmentDate}})
                bodyParams.Add(New Dictionary(Of String, String) From {{"type", "text"}, {"text", params.AppointmentHour}})
                bodyParams.Add(New Dictionary(Of String, String) From {{"type", "text"}, {"text", params.DoctorName}})
                bodyParams.Add(New Dictionary(Of String, String) From {{"type", "text"}, {"text", params.Office}})
                bodyParams.Add(New Dictionary(Of String, String) From {{"type", "text"}, {"text", params.MedicalPhone}})

            Case "recordatorio_cita"
                bodyParams.Add(New Dictionary(Of String, String) From {{"type", "text"}, {"text", params.UserName}})
                bodyParams.Add(New Dictionary(Of String, String) From {{"type", "text"}, {"text", params.AppointmentDate}})
                bodyParams.Add(New Dictionary(Of String, String) From {{"type", "text"}, {"text", params.AppointmentHour}})

            Case "reagendar_cita"
                bodyParams.Add(New Dictionary(Of String, String) From {{"type", "text"}, {"text", params.UserName}})
                bodyParams.Add(New Dictionary(Of String, String) From {{"type", "text"}, {"text", params.AppointmentDate}})
                bodyParams.Add(New Dictionary(Of String, String) From {{"type", "text"}, {"text", params.AppointmentHour}})
                bodyParams.Add(New Dictionary(Of String, String) From {{"type", "text"}, {"text", params.AppointmentNewDate}})
                bodyParams.Add(New Dictionary(Of String, String) From {{"type", "text"}, {"text", params.AppointmentNewHour}})

            Case "orden_internamiento_tratamiento", "ingresos_clinica"
                bodyParams.Add(New Dictionary(Of String, String) From {{"type", "text"}, {"text", params.UserName}})
                bodyParams.Add(New Dictionary(Of String, String) From {{"type", "text"}, {"text", params.TipoIngreso}})
                bodyParams.Add(New Dictionary(Of String, String) From {{"type", "text"}, {"text", params.MedicalUnit}})
                bodyParams.Add(New Dictionary(Of String, String) From {{"type", "text"}, {"text", params.MedicalAdress}})
                bodyParams.Add(New Dictionary(Of String, String) From {{"type", "text"}, {"text", params.MedicalPhone}})
                bodyParams.Add(New Dictionary(Of String, String) From {{"type", "text"}, {"text", params.FolioCitaOI}})
                bodyParams.Add(New Dictionary(Of String, String) From {{"type", "text"}, {"text", params.AppointmentDate}})
                bodyParams.Add(New Dictionary(Of String, String) From {{"type", "text"}, {"text", params.AppointmentHour}})
                bodyParams.Add(New Dictionary(Of String, String) From {{"type", "text"}, {"text", params.AppointmentIssue}})
                bodyParams.Add(New Dictionary(Of String, String) From {{"type", "text"}, {"text", params.IndicacionesPacienteOI}})
                bodyParams.Add(New Dictionary(Of String, String) From {{"type", "text"}, {"text", params.TiempoPresentarseAntesIngreso}})
        End Select

        If bodyParams.Count > 0 Then
            components.Add(New Dictionary(Of String, Object) From {
                {"type", "body"},
                {"parameters", bodyParams}
            })
        End If

        Dim requestData As New Dictionary(Of String, Object) From {
            {"messaging_product", "whatsapp"},
            {"to", params.ToNumber},
            {"type", "template"},
            {"template", New Dictionary(Of String, Object) From {
                {"name", params.TemplateName},
                {"language", New Dictionary(Of String, Object) From {
                    {"code", params.LanguageCode}
                }},
                {"components", components}
            }}
        }

        Return JsonConvert.SerializeObject(requestData)

    End Function

End Class

Public Class Parameters

    Public Property AccessToken As String
    Public Property PhoneNumberID As String
    Public Property ToNumber As String
    Public Property TemplateName As String
    Public Property LanguageCode As String
    Public Property UserName As String
    Public Property MedicalUnit As String
    Public Property AppointmentDate As String
    Public Property AppointmentHour As String
    Public Property DoctorName As String
    Public Property Office As String
    Public Property AppointmentNewDate As String
    Public Property AppointmentNewHour As String
    Public Property TipoIngreso As String
    Public Property TiempoPresentarseAntesIngreso As String
    Public Property IndicacionesPacienteOI As String
    Public Property HeaderImageURL As String
    Public Property MedicalAdress As String
    Public Property MedicalPhone As String
    Public Property AppointmentIssue As String
    Public Property FolioCitaOI As String

End Class
