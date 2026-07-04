<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()> _
Partial Class frmInterface
    Inherits System.Windows.Forms.Form

    'Form reemplaza a Dispose para limpiar la lista de componentes.
    <System.Diagnostics.DebuggerNonUserCode()> _
    Protected Overrides Sub Dispose(ByVal disposing As Boolean)
        Try
            If disposing AndAlso components IsNot Nothing Then
                components.Dispose()
            End If
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    'Requerido por el Diseñador de Windows Forms
    Private components As System.ComponentModel.IContainer

    'NOTA: el Diseñador de Windows Forms necesita el siguiente procedimiento
    'Se puede modificar usando el Diseñador de Windows Forms.  
    'No lo modifique con el editor de código.
    <System.Diagnostics.DebuggerStepThrough()> _
    Private Sub InitializeComponent()
        Me.components = New System.ComponentModel.Container()
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(frmInterface))
        Me.HistoMedicRPA = New System.Windows.Forms.NotifyIcon(Me.components)
        Me.ContextMenuStrip1 = New System.Windows.Forms.ContextMenuStrip(Me.components)
        Me.MenuAbrir = New System.Windows.Forms.ToolStripMenuItem()
        Me.ToolStripSeparator2 = New System.Windows.Forms.ToolStripSeparator()
        Me.MenuCerrar = New System.Windows.Forms.ToolStripMenuItem()
        Me.btnConectarDBCentral = New DevComponents.DotNetBar.ButtonX()
        Me.LayoutControlItem6 = New DevComponents.DotNetBar.Layout.LayoutControlItem()
        Me.LayoutControlItem8 = New DevComponents.DotNetBar.Layout.LayoutControlItem()
        Me.LayoutControlItem7 = New DevComponents.DotNetBar.Layout.LayoutControlItem()
        Me.ExpandablePanel2 = New DevComponents.DotNetBar.ExpandablePanel()
        Me.lstLog = New DevComponents.DotNetBar.Controls.ListViewEx()
        Me.FechaHora = CType(New System.Windows.Forms.ColumnHeader(), System.Windows.Forms.ColumnHeader)
        Me.LogdeEnlace = CType(New System.Windows.Forms.ColumnHeader(), System.Windows.Forms.ColumnHeader)
        Me.ProgressBarX_SPALM = New DevComponents.DotNetBar.Controls.ProgressBarX()
        Me.ProgressBarX_SP = New DevComponents.DotNetBar.Controls.ProgressBarX()
        Me.ProgressBarX1 = New DevComponents.DotNetBar.Controls.ProgressBarX()
        Me.LayoutControlItem9 = New DevComponents.DotNetBar.Layout.LayoutControlItem()
        Me.LayoutControlItem10 = New DevComponents.DotNetBar.Layout.LayoutControlItem()
        Me.LayoutControlItem11 = New DevComponents.DotNetBar.Layout.LayoutControlItem()
        Me.LayoutControlItem12 = New DevComponents.DotNetBar.Layout.LayoutControlItem()
        Me.LayoutControlItem13 = New DevComponents.DotNetBar.Layout.LayoutControlItem()
        Me.btnConectarLocal = New DevComponents.DotNetBar.ButtonX()
        Me.PictureBox1 = New System.Windows.Forms.PictureBox()
        Me.TimerEnlace = New System.Windows.Forms.Timer(Me.components)
        Me.LayoutControlItem14 = New DevComponents.DotNetBar.Layout.LayoutControlItem()
        Me.LayoutControlItem15 = New DevComponents.DotNetBar.Layout.LayoutControlItem()
        Me.LayoutControlItem16 = New DevComponents.DotNetBar.Layout.LayoutControlItem()
        Me.LayoutControlItem17 = New DevComponents.DotNetBar.Layout.LayoutControlItem()
        Me.LayoutControlItem18 = New DevComponents.DotNetBar.Layout.LayoutControlItem()
        Me.LayoutControlItem5 = New DevComponents.DotNetBar.Layout.LayoutControlItem()
        Me.chkActivar = New DevComponents.DotNetBar.Controls.CheckBoxX()
        Me.Label47 = New System.Windows.Forms.Label()
        Me.txtLimitRegistros = New DevComponents.DotNetBar.Controls.TextBoxX()
        Me.ContextMenuStrip1.SuspendLayout()
        Me.ExpandablePanel2.SuspendLayout()
        CType(Me.PictureBox1, System.ComponentModel.ISupportInitialize).BeginInit()
        Me.SuspendLayout()
        '
        'HistoMedicRPA
        '
        Me.HistoMedicRPA.ContextMenuStrip = Me.ContextMenuStrip1
        Me.HistoMedicRPA.Icon = CType(resources.GetObject("HistoMedicRPA.Icon"), System.Drawing.Icon)
        Me.HistoMedicRPA.Text = "RPA HistoMedic"
        Me.HistoMedicRPA.Visible = True
        '
        'ContextMenuStrip1
        '
        Me.ContextMenuStrip1.Items.AddRange(New System.Windows.Forms.ToolStripItem() {Me.MenuAbrir, Me.ToolStripSeparator2, Me.MenuCerrar})
        Me.ContextMenuStrip1.Name = "ContextMenuStrip1"
        Me.ContextMenuStrip1.Size = New System.Drawing.Size(166, 54)
        '
        'MenuAbrir
        '
        Me.MenuAbrir.ForeColor = System.Drawing.Color.FromArgb(CType(CType(15, Byte), Integer), CType(CType(10, Byte), Integer), CType(CType(74, Byte), Integer))
        Me.MenuAbrir.Image = CType(resources.GetObject("MenuAbrir.Image"), System.Drawing.Image)
        Me.MenuAbrir.Name = "MenuAbrir"
        Me.MenuAbrir.Size = New System.Drawing.Size(165, 22)
        Me.MenuAbrir.Text = "Abrir"
        '
        'ToolStripSeparator2
        '
        Me.ToolStripSeparator2.ForeColor = System.Drawing.Color.FromArgb(CType(CType(15, Byte), Integer), CType(CType(10, Byte), Integer), CType(CType(74, Byte), Integer))
        Me.ToolStripSeparator2.Name = "ToolStripSeparator2"
        Me.ToolStripSeparator2.Size = New System.Drawing.Size(162, 6)
        '
        'MenuCerrar
        '
        Me.MenuCerrar.ForeColor = System.Drawing.Color.FromArgb(CType(CType(15, Byte), Integer), CType(CType(10, Byte), Integer), CType(CType(74, Byte), Integer))
        Me.MenuCerrar.Image = CType(resources.GetObject("MenuCerrar.Image"), System.Drawing.Image)
        Me.MenuCerrar.Name = "MenuCerrar"
        Me.MenuCerrar.Size = New System.Drawing.Size(165, 22)
        Me.MenuCerrar.Text = "Cerrra Aplicación"
        '
        'btnConectarDBCentral
        '
        Me.btnConectarDBCentral.AccessibleRole = System.Windows.Forms.AccessibleRole.PushButton
        Me.btnConectarDBCentral.ColorTable = DevComponents.DotNetBar.eButtonColor.Flat
        Me.btnConectarDBCentral.Font = New System.Drawing.Font("MS Reference Sans Serif", 8.25!, System.Drawing.FontStyle.Bold)
        Me.btnConectarDBCentral.Image = CType(resources.GetObject("btnConectarDBCentral.Image"), System.Drawing.Image)
        Me.btnConectarDBCentral.Location = New System.Drawing.Point(242, 78)
        Me.btnConectarDBCentral.Margin = New System.Windows.Forms.Padding(0)
        Me.btnConectarDBCentral.Name = "btnConectarDBCentral"
        Me.btnConectarDBCentral.Size = New System.Drawing.Size(259, 35)
        Me.btnConectarDBCentral.Style = DevComponents.DotNetBar.eDotNetBarStyle.StyleManagerControlled
        Me.btnConectarDBCentral.TabIndex = 1
        Me.btnConectarDBCentral.Tag = ""
        Me.btnConectarDBCentral.Text = "Estatus DB Central"
        Me.btnConectarDBCentral.TextColor = System.Drawing.Color.FromArgb(CType(CType(15, Byte), Integer), CType(CType(10, Byte), Integer), CType(CType(74, Byte), Integer))
        '
        'LayoutControlItem6
        '
        Me.LayoutControlItem6.Height = 43
        Me.LayoutControlItem6.MinSize = New System.Drawing.Size(32, 20)
        Me.LayoutControlItem6.Name = "LayoutControlItem6"
        Me.LayoutControlItem6.Style.BorderThickness = New DevComponents.DotNetBar.Layout.Thickness(0.0R, 0.0R, 0.0R, 0.0R)
        Me.LayoutControlItem6.Width = 50
        Me.LayoutControlItem6.WidthType = DevComponents.DotNetBar.Layout.eLayoutSizeType.Percent
        '
        'LayoutControlItem8
        '
        Me.LayoutControlItem8.Height = 43
        Me.LayoutControlItem8.MinSize = New System.Drawing.Size(32, 20)
        Me.LayoutControlItem8.Name = "LayoutControlItem8"
        Me.LayoutControlItem8.Style.BorderThickness = New DevComponents.DotNetBar.Layout.Thickness(0.0R, 0.0R, 0.0R, 0.0R)
        Me.LayoutControlItem8.Width = 33
        Me.LayoutControlItem8.WidthType = DevComponents.DotNetBar.Layout.eLayoutSizeType.Percent
        '
        'LayoutControlItem7
        '
        Me.LayoutControlItem7.Height = 19
        Me.LayoutControlItem7.MinSize = New System.Drawing.Size(64, 18)
        Me.LayoutControlItem7.Name = "LayoutControlItem7"
        Me.LayoutControlItem7.Style.BorderThickness = New DevComponents.DotNetBar.Layout.Thickness(0.0R, 0.0R, 0.0R, 0.0R)
        Me.LayoutControlItem7.Text = "Label:"
        Me.LayoutControlItem7.Width = 532
        '
        'ExpandablePanel2
        '
        Me.ExpandablePanel2.Anchor = CType((((System.Windows.Forms.AnchorStyles.Top Or System.Windows.Forms.AnchorStyles.Bottom) _
            Or System.Windows.Forms.AnchorStyles.Left) _
            Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.ExpandablePanel2.CanvasColor = System.Drawing.SystemColors.Control
        Me.ExpandablePanel2.ColorSchemeStyle = DevComponents.DotNetBar.eDotNetBarStyle.Metro
        Me.ExpandablePanel2.Controls.Add(Me.lstLog)
        Me.ExpandablePanel2.Controls.Add(Me.ProgressBarX_SPALM)
        Me.ExpandablePanel2.Controls.Add(Me.ProgressBarX_SP)
        Me.ExpandablePanel2.Controls.Add(Me.ProgressBarX1)
        Me.ExpandablePanel2.Font = New System.Drawing.Font("MS Reference Sans Serif", 8.25!)
        Me.ExpandablePanel2.HideControlsWhenCollapsed = True
        Me.ExpandablePanel2.Location = New System.Drawing.Point(9, 147)
        Me.ExpandablePanel2.Name = "ExpandablePanel2"
        Me.ExpandablePanel2.Size = New System.Drawing.Size(492, 370)
        Me.ExpandablePanel2.Style.Alignment = System.Drawing.StringAlignment.Center
        Me.ExpandablePanel2.Style.BackColor1.ColorSchemePart = DevComponents.DotNetBar.eColorSchemePart.BarBackground
        Me.ExpandablePanel2.Style.Border = DevComponents.DotNetBar.eBorderType.SingleLine
        Me.ExpandablePanel2.Style.ForeColor.ColorSchemePart = DevComponents.DotNetBar.eColorSchemePart.ItemText
        Me.ExpandablePanel2.Style.GradientAngle = 90
        Me.ExpandablePanel2.StyleMouseDown.Alignment = System.Drawing.StringAlignment.Center
        Me.ExpandablePanel2.StyleMouseDown.BackColor1.ColorSchemePart = DevComponents.DotNetBar.eColorSchemePart.ItemPressedBackground
        Me.ExpandablePanel2.StyleMouseDown.BorderColor.ColorSchemePart = DevComponents.DotNetBar.eColorSchemePart.ItemPressedBorder
        Me.ExpandablePanel2.StyleMouseDown.ForeColor.ColorSchemePart = DevComponents.DotNetBar.eColorSchemePart.ItemPressedText
        Me.ExpandablePanel2.StyleMouseOver.Alignment = System.Drawing.StringAlignment.Center
        Me.ExpandablePanel2.StyleMouseOver.BackColor1.ColorSchemePart = DevComponents.DotNetBar.eColorSchemePart.ItemHotBackground
        Me.ExpandablePanel2.StyleMouseOver.BorderColor.ColorSchemePart = DevComponents.DotNetBar.eColorSchemePart.ItemHotBorder
        Me.ExpandablePanel2.StyleMouseOver.ForeColor.ColorSchemePart = DevComponents.DotNetBar.eColorSchemePart.ItemHotText
        Me.ExpandablePanel2.TabIndex = 19
        Me.ExpandablePanel2.TitleStyle.BackColor1.ColorSchemePart = DevComponents.DotNetBar.eColorSchemePart.PanelBackground
        Me.ExpandablePanel2.TitleStyle.Border = DevComponents.DotNetBar.eBorderType.RaisedInner
        Me.ExpandablePanel2.TitleStyle.BorderColor.ColorSchemePart = DevComponents.DotNetBar.eColorSchemePart.PanelBorder
        Me.ExpandablePanel2.TitleStyle.ForeColor.ColorSchemePart = DevComponents.DotNetBar.eColorSchemePart.PanelText
        Me.ExpandablePanel2.TitleStyle.GradientAngle = 90
        Me.ExpandablePanel2.TitleText = "   Log"
        '
        'lstLog
        '
        Me.lstLog.BackColor = System.Drawing.Color.White
        '
        '
        '
        Me.lstLog.Border.Class = "ListViewBorder"
        Me.lstLog.Border.CornerType = DevComponents.DotNetBar.eCornerType.Square
        Me.lstLog.Columns.AddRange(New System.Windows.Forms.ColumnHeader() {Me.FechaHora, Me.LogdeEnlace})
        Me.lstLog.Dock = System.Windows.Forms.DockStyle.Fill
        Me.lstLog.ForeColor = System.Drawing.Color.FromArgb(CType(CType(15, Byte), Integer), CType(CType(10, Byte), Integer), CType(CType(74, Byte), Integer))
        Me.lstLog.FullRowSelect = True
        Me.lstLog.GridLines = True
        Me.lstLog.Location = New System.Drawing.Point(0, 89)
        Me.lstLog.Name = "lstLog"
        Me.lstLog.Size = New System.Drawing.Size(492, 281)
        Me.lstLog.TabIndex = 1
        Me.lstLog.UseCompatibleStateImageBehavior = False
        Me.lstLog.View = System.Windows.Forms.View.Details
        '
        'FechaHora
        '
        Me.FechaHora.Text = "Fecha Hora"
        Me.FechaHora.Width = 100
        '
        'LogdeEnlace
        '
        Me.LogdeEnlace.Text = "Log de Enlace"
        Me.LogdeEnlace.Width = 750
        '
        'ProgressBarX_SPALM
        '
        '
        '
        '
        Me.ProgressBarX_SPALM.BackgroundStyle.CornerType = DevComponents.DotNetBar.eCornerType.Square
        Me.ProgressBarX_SPALM.Dock = System.Windows.Forms.DockStyle.Top
        Me.ProgressBarX_SPALM.Location = New System.Drawing.Point(0, 68)
        Me.ProgressBarX_SPALM.Name = "ProgressBarX_SPALM"
        Me.ProgressBarX_SPALM.Size = New System.Drawing.Size(492, 21)
        Me.ProgressBarX_SPALM.TabIndex = 4
        Me.ProgressBarX_SPALM.TextVisible = True
        '
        'ProgressBarX_SP
        '
        '
        '
        '
        Me.ProgressBarX_SP.BackgroundStyle.CornerType = DevComponents.DotNetBar.eCornerType.Square
        Me.ProgressBarX_SP.Dock = System.Windows.Forms.DockStyle.Top
        Me.ProgressBarX_SP.Location = New System.Drawing.Point(0, 47)
        Me.ProgressBarX_SP.Name = "ProgressBarX_SP"
        Me.ProgressBarX_SP.Size = New System.Drawing.Size(492, 21)
        Me.ProgressBarX_SP.TabIndex = 3
        Me.ProgressBarX_SP.TextVisible = True
        '
        'ProgressBarX1
        '
        '
        '
        '
        Me.ProgressBarX1.BackgroundStyle.CornerType = DevComponents.DotNetBar.eCornerType.Square
        Me.ProgressBarX1.Dock = System.Windows.Forms.DockStyle.Top
        Me.ProgressBarX1.Location = New System.Drawing.Point(0, 26)
        Me.ProgressBarX1.Name = "ProgressBarX1"
        Me.ProgressBarX1.Size = New System.Drawing.Size(492, 21)
        Me.ProgressBarX1.TabIndex = 2
        Me.ProgressBarX1.TextVisible = True
        '
        'LayoutControlItem9
        '
        Me.LayoutControlItem9.Height = 29
        Me.LayoutControlItem9.MinSize = New System.Drawing.Size(120, 0)
        Me.LayoutControlItem9.Name = "LayoutControlItem9"
        Me.LayoutControlItem9.Style.BorderThickness = New DevComponents.DotNetBar.Layout.Thickness(0.0R, 0.0R, 0.0R, 0.0R)
        Me.LayoutControlItem9.Text = "Servidor:"
        Me.LayoutControlItem9.Width = 50
        Me.LayoutControlItem9.WidthType = DevComponents.DotNetBar.Layout.eLayoutSizeType.Percent
        '
        'LayoutControlItem10
        '
        Me.LayoutControlItem10.Height = 29
        Me.LayoutControlItem10.MinSize = New System.Drawing.Size(120, 0)
        Me.LayoutControlItem10.Name = "LayoutControlItem10"
        Me.LayoutControlItem10.Style.BorderThickness = New DevComponents.DotNetBar.Layout.Thickness(0.0R, 0.0R, 0.0R, 0.0R)
        Me.LayoutControlItem10.Text = "Puerto:"
        Me.LayoutControlItem10.Width = 50
        Me.LayoutControlItem10.WidthType = DevComponents.DotNetBar.Layout.eLayoutSizeType.Percent
        '
        'LayoutControlItem11
        '
        Me.LayoutControlItem11.Height = 29
        Me.LayoutControlItem11.MinSize = New System.Drawing.Size(120, 0)
        Me.LayoutControlItem11.Name = "LayoutControlItem11"
        Me.LayoutControlItem11.Style.BorderThickness = New DevComponents.DotNetBar.Layout.Thickness(0.0R, 0.0R, 0.0R, 0.0R)
        Me.LayoutControlItem11.Text = "Usuario:"
        Me.LayoutControlItem11.Width = 50
        Me.LayoutControlItem11.WidthType = DevComponents.DotNetBar.Layout.eLayoutSizeType.Percent
        '
        'LayoutControlItem12
        '
        Me.LayoutControlItem12.Height = 29
        Me.LayoutControlItem12.MinSize = New System.Drawing.Size(120, 0)
        Me.LayoutControlItem12.Name = "LayoutControlItem12"
        Me.LayoutControlItem12.Style.BorderThickness = New DevComponents.DotNetBar.Layout.Thickness(0.0R, 0.0R, 0.0R, 0.0R)
        Me.LayoutControlItem12.Text = "Contraseña:"
        Me.LayoutControlItem12.Width = 50
        Me.LayoutControlItem12.WidthType = DevComponents.DotNetBar.Layout.eLayoutSizeType.Percent
        '
        'LayoutControlItem13
        '
        Me.LayoutControlItem13.Height = 43
        Me.LayoutControlItem13.MinSize = New System.Drawing.Size(32, 20)
        Me.LayoutControlItem13.Name = "LayoutControlItem13"
        Me.LayoutControlItem13.Style.BorderThickness = New DevComponents.DotNetBar.Layout.Thickness(0.0R, 0.0R, 0.0R, 0.0R)
        Me.LayoutControlItem13.Width = 50
        Me.LayoutControlItem13.WidthType = DevComponents.DotNetBar.Layout.eLayoutSizeType.Percent
        '
        'btnConectarLocal
        '
        Me.btnConectarLocal.AccessibleRole = System.Windows.Forms.AccessibleRole.PushButton
        Me.btnConectarLocal.ColorTable = DevComponents.DotNetBar.eButtonColor.Flat
        Me.btnConectarLocal.Font = New System.Drawing.Font("MS Reference Sans Serif", 8.25!, System.Drawing.FontStyle.Bold)
        Me.btnConectarLocal.Image = CType(resources.GetObject("btnConectarLocal.Image"), System.Drawing.Image)
        Me.btnConectarLocal.Location = New System.Drawing.Point(9, 78)
        Me.btnConectarLocal.Margin = New System.Windows.Forms.Padding(0)
        Me.btnConectarLocal.Name = "btnConectarLocal"
        Me.btnConectarLocal.Size = New System.Drawing.Size(233, 35)
        Me.btnConectarLocal.Style = DevComponents.DotNetBar.eDotNetBarStyle.StyleManagerControlled
        Me.btnConectarLocal.TabIndex = 3
        Me.btnConectarLocal.Tag = ""
        Me.btnConectarLocal.Text = "Estatus DB Local"
        Me.btnConectarLocal.TextColor = System.Drawing.Color.FromArgb(CType(CType(15, Byte), Integer), CType(CType(10, Byte), Integer), CType(CType(74, Byte), Integer))
        '
        'PictureBox1
        '
        Me.PictureBox1.BackColor = System.Drawing.Color.White
        Me.PictureBox1.Dock = System.Windows.Forms.DockStyle.Top
        Me.PictureBox1.Image = CType(resources.GetObject("PictureBox1.Image"), System.Drawing.Image)
        Me.PictureBox1.Location = New System.Drawing.Point(0, 0)
        Me.PictureBox1.Name = "PictureBox1"
        Me.PictureBox1.Size = New System.Drawing.Size(511, 71)
        Me.PictureBox1.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom
        Me.PictureBox1.TabIndex = 32
        Me.PictureBox1.TabStop = False
        '
        'TimerEnlace
        '
        Me.TimerEnlace.Interval = 1000
        '
        'LayoutControlItem14
        '
        Me.LayoutControlItem14.Height = 29
        Me.LayoutControlItem14.MinSize = New System.Drawing.Size(120, 0)
        Me.LayoutControlItem14.Name = "LayoutControlItem14"
        Me.LayoutControlItem14.Style.BorderThickness = New DevComponents.DotNetBar.Layout.Thickness(0.0R, 0.0R, 0.0R, 0.0R)
        Me.LayoutControlItem14.Text = "Servidor:"
        Me.LayoutControlItem14.Width = 50
        Me.LayoutControlItem14.WidthType = DevComponents.DotNetBar.Layout.eLayoutSizeType.Percent
        '
        'LayoutControlItem15
        '
        Me.LayoutControlItem15.Height = 29
        Me.LayoutControlItem15.MinSize = New System.Drawing.Size(120, 0)
        Me.LayoutControlItem15.Name = "LayoutControlItem15"
        Me.LayoutControlItem15.Style.BorderThickness = New DevComponents.DotNetBar.Layout.Thickness(0.0R, 0.0R, 0.0R, 0.0R)
        Me.LayoutControlItem15.Text = "Puerto:"
        Me.LayoutControlItem15.Width = 50
        Me.LayoutControlItem15.WidthType = DevComponents.DotNetBar.Layout.eLayoutSizeType.Percent
        '
        'LayoutControlItem16
        '
        Me.LayoutControlItem16.Height = 29
        Me.LayoutControlItem16.MinSize = New System.Drawing.Size(120, 0)
        Me.LayoutControlItem16.Name = "LayoutControlItem16"
        Me.LayoutControlItem16.Style.BorderThickness = New DevComponents.DotNetBar.Layout.Thickness(0.0R, 0.0R, 0.0R, 0.0R)
        Me.LayoutControlItem16.Text = "Usuario:"
        Me.LayoutControlItem16.Width = 50
        Me.LayoutControlItem16.WidthType = DevComponents.DotNetBar.Layout.eLayoutSizeType.Percent
        '
        'LayoutControlItem17
        '
        Me.LayoutControlItem17.Height = 29
        Me.LayoutControlItem17.MinSize = New System.Drawing.Size(120, 0)
        Me.LayoutControlItem17.Name = "LayoutControlItem17"
        Me.LayoutControlItem17.Style.BorderThickness = New DevComponents.DotNetBar.Layout.Thickness(0.0R, 0.0R, 0.0R, 0.0R)
        Me.LayoutControlItem17.Text = "Contraseña:"
        Me.LayoutControlItem17.Width = 50
        Me.LayoutControlItem17.WidthType = DevComponents.DotNetBar.Layout.eLayoutSizeType.Percent
        '
        'LayoutControlItem18
        '
        Me.LayoutControlItem18.Height = 43
        Me.LayoutControlItem18.MinSize = New System.Drawing.Size(32, 20)
        Me.LayoutControlItem18.Name = "LayoutControlItem18"
        Me.LayoutControlItem18.Style.BorderThickness = New DevComponents.DotNetBar.Layout.Thickness(0.0R, 0.0R, 0.0R, 0.0R)
        Me.LayoutControlItem18.Width = 50
        Me.LayoutControlItem18.WidthType = DevComponents.DotNetBar.Layout.eLayoutSizeType.Percent
        '
        'LayoutControlItem5
        '
        Me.LayoutControlItem5.Height = 43
        Me.LayoutControlItem5.MinSize = New System.Drawing.Size(32, 20)
        Me.LayoutControlItem5.Name = "LayoutControlItem5"
        Me.LayoutControlItem5.Style.BorderThickness = New DevComponents.DotNetBar.Layout.Thickness(0.0R, 0.0R, 0.0R, 0.0R)
        Me.LayoutControlItem5.Width = 50
        Me.LayoutControlItem5.WidthType = DevComponents.DotNetBar.Layout.eLayoutSizeType.Percent
        '
        'chkActivar
        '
        Me.chkActivar.BackColor = System.Drawing.Color.Transparent
        '
        '
        '
        Me.chkActivar.BackgroundStyle.CornerType = DevComponents.DotNetBar.eCornerType.Square
        Me.chkActivar.Font = New System.Drawing.Font("MS Reference Sans Serif", 8.25!)
        Me.chkActivar.Location = New System.Drawing.Point(12, 120)
        Me.chkActivar.Name = "chkActivar"
        Me.chkActivar.Size = New System.Drawing.Size(239, 21)
        Me.chkActivar.Style = DevComponents.DotNetBar.eDotNetBarStyle.StyleManagerControlled
        Me.chkActivar.TabIndex = 20
        Me.chkActivar.Text = "Activar Secuencia de Sincronización."
        Me.chkActivar.TextColor = System.Drawing.Color.FromArgb(CType(CType(15, Byte), Integer), CType(CType(10, Byte), Integer), CType(CType(74, Byte), Integer))
        '
        'Label47
        '
        Me.Label47.BackColor = System.Drawing.Color.Transparent
        Me.Label47.Font = New System.Drawing.Font("MS Reference Sans Serif", 8.25!)
        Me.Label47.ForeColor = System.Drawing.Color.FromArgb(CType(CType(15, Byte), Integer), CType(CType(10, Byte), Integer), CType(CType(74, Byte), Integer))
        Me.Label47.Location = New System.Drawing.Point(257, 120)
        Me.Label47.Name = "Label47"
        Me.Label47.Size = New System.Drawing.Size(173, 21)
        Me.Label47.TabIndex = 33
        Me.Label47.Text = "Registros:"
        Me.Label47.TextAlign = System.Drawing.ContentAlignment.MiddleRight
        '
        'txtLimitRegistros
        '
        Me.txtLimitRegistros.BackColor = System.Drawing.Color.White
        '
        '
        '
        Me.txtLimitRegistros.Border.Class = "TextBoxBorder"
        Me.txtLimitRegistros.Border.CornerType = DevComponents.DotNetBar.eCornerType.Square
        Me.txtLimitRegistros.FocusHighlightColor = System.Drawing.Color.FromArgb(CType(CType(230, Byte), Integer), CType(CType(255, Byte), Integer), CType(CType(230, Byte), Integer))
        Me.txtLimitRegistros.FocusHighlightEnabled = True
        Me.txtLimitRegistros.Font = New System.Drawing.Font("MS Reference Sans Serif", 8.25!)
        Me.txtLimitRegistros.ForeColor = System.Drawing.Color.Black
        Me.txtLimitRegistros.Location = New System.Drawing.Point(436, 120)
        Me.txtLimitRegistros.Name = "txtLimitRegistros"
        Me.txtLimitRegistros.Size = New System.Drawing.Size(65, 21)
        Me.txtLimitRegistros.TabIndex = 34
        Me.txtLimitRegistros.Text = "2000"
        Me.txtLimitRegistros.TextAlign = System.Windows.Forms.HorizontalAlignment.Center
        '
        'frmInterface
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(6.0!, 13.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.BackColor = System.Drawing.Color.White
        Me.BackgroundImage = CType(resources.GetObject("$this.BackgroundImage"), System.Drawing.Image)
        Me.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom
        Me.ClientSize = New System.Drawing.Size(511, 525)
        Me.Controls.Add(Me.Label47)
        Me.Controls.Add(Me.txtLimitRegistros)
        Me.Controls.Add(Me.chkActivar)
        Me.Controls.Add(Me.ExpandablePanel2)
        Me.Controls.Add(Me.btnConectarLocal)
        Me.Controls.Add(Me.btnConectarDBCentral)
        Me.Controls.Add(Me.PictureBox1)
        Me.DoubleBuffered = True
        Me.ForeColor = System.Drawing.Color.FromArgb(CType(CType(15, Byte), Integer), CType(CType(10, Byte), Integer), CType(CType(74, Byte), Integer))
        Me.Icon = CType(resources.GetObject("$this.Icon"), System.Drawing.Icon)
        Me.KeyPreview = True
        Me.Name = "frmInterface"
        Me.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen
        Me.Text = "Enlace HistoMedic Local <-> Hosting"
        Me.ContextMenuStrip1.ResumeLayout(False)
        Me.ExpandablePanel2.ResumeLayout(False)
        CType(Me.PictureBox1, System.ComponentModel.ISupportInitialize).EndInit()
        Me.ResumeLayout(False)

    End Sub
    Friend WithEvents HistoMedicRPA As System.Windows.Forms.NotifyIcon
    Friend WithEvents btnConectarDBCentral As DevComponents.DotNetBar.ButtonX
    Friend WithEvents LayoutControlItem8 As DevComponents.DotNetBar.Layout.LayoutControlItem
    Friend WithEvents LayoutControlItem6 As DevComponents.DotNetBar.Layout.LayoutControlItem
    Friend WithEvents LayoutControlItem7 As DevComponents.DotNetBar.Layout.LayoutControlItem
    Friend WithEvents ContextMenuStrip1 As System.Windows.Forms.ContextMenuStrip
    Friend WithEvents MenuAbrir As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents ExpandablePanel2 As DevComponents.DotNetBar.ExpandablePanel
    Friend WithEvents LayoutControlItem9 As DevComponents.DotNetBar.Layout.LayoutControlItem
    Friend WithEvents LayoutControlItem10 As DevComponents.DotNetBar.Layout.LayoutControlItem
    Friend WithEvents LayoutControlItem11 As DevComponents.DotNetBar.Layout.LayoutControlItem
    Friend WithEvents LayoutControlItem12 As DevComponents.DotNetBar.Layout.LayoutControlItem
    Friend WithEvents LayoutControlItem13 As DevComponents.DotNetBar.Layout.LayoutControlItem
    Friend WithEvents lstLog As DevComponents.DotNetBar.Controls.ListViewEx
    Friend WithEvents ProgressBarX1 As DevComponents.DotNetBar.Controls.ProgressBarX
    Friend WithEvents TimerEnlace As System.Windows.Forms.Timer
    Friend WithEvents LayoutControlItem14 As DevComponents.DotNetBar.Layout.LayoutControlItem
    Friend WithEvents LayoutControlItem15 As DevComponents.DotNetBar.Layout.LayoutControlItem
    Friend WithEvents LayoutControlItem16 As DevComponents.DotNetBar.Layout.LayoutControlItem
    Friend WithEvents LayoutControlItem17 As DevComponents.DotNetBar.Layout.LayoutControlItem
    Friend WithEvents LayoutControlItem18 As DevComponents.DotNetBar.Layout.LayoutControlItem
    Friend WithEvents ToolStripSeparator2 As System.Windows.Forms.ToolStripSeparator
    Friend WithEvents MenuCerrar As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents LayoutControlItem5 As DevComponents.DotNetBar.Layout.LayoutControlItem
    Friend WithEvents btnConectarLocal As DevComponents.DotNetBar.ButtonX
    Friend WithEvents PictureBox1 As System.Windows.Forms.PictureBox
    Friend WithEvents chkActivar As DevComponents.DotNetBar.Controls.CheckBoxX
    Friend WithEvents Label47 As System.Windows.Forms.Label
    Friend WithEvents txtLimitRegistros As DevComponents.DotNetBar.Controls.TextBoxX
    Friend WithEvents FechaHora As System.Windows.Forms.ColumnHeader
    Friend WithEvents LogdeEnlace As System.Windows.Forms.ColumnHeader
    Friend WithEvents ProgressBarX_SPALM As DevComponents.DotNetBar.Controls.ProgressBarX
    Friend WithEvents ProgressBarX_SP As DevComponents.DotNetBar.Controls.ProgressBarX

End Class
