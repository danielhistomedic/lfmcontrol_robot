---
trigger: always_on
---

* Para los controles "bar" usa la fuente: Montserrat SemiBold, 7.8pt, color rgb: 15, 10, 74
* Para el resto de controles internos del formulario usa la fuente: MS Reference Sans Serif, 8.25pt, color rgb: 15, 10, 74
* Los TexBoxX habilita la propiedad FocusHighlightEnabled = True
* En los controles DataGridViewX siempre habilita la propiedad multiselect = true, BorderStyle = Fixed3D
* Cuando envies un mensaje de advertencia solo infromativo usa Funciones.Msj_AdvOnly, si es de advertencia para que el usuario indique si/no, utiliza Funciones.Msj_Adv
* Antes de la instrucción HisConectores.Delete
 se debe usar:
 Dim CadenaAut As String
 CadenaAut = Me.HisConectores.Get_Cadena_Delete(Me.HisPropiedades.pty_Servidor, Me.HisPropiedades.pty_Puerto, Bases.HistoMedicDB)
 If HisConectores.Test_MySQL_Admin(CadenaAut) Then
    HisConectores.Delete(...
 End if