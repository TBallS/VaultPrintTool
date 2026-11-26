Imports System
Imports System.Collections.Generic
Imports System.Collections.ObjectModel
Imports System.Diagnostics
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Net
Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Reflection
Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks
Imports System.Web.Script.Serialization
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Input
Imports System.Windows.Media
Imports System.Windows.Media.Animation
Imports System.Windows.Markup
Imports System.Windows.Threading
Imports System.Xml
Imports PdfiumViewer
Imports System.Drawing.Printing

<Assembly: AssemblyTitle("Vault Print Tool")>
<Assembly: AssemblyProduct("Vault Print Tool")>
<Assembly: AssemblyVersion("3.0.0.0")>
<Assembly: AssemblyFileVersion("3.0.0.0")>

' Entry Point
Public Module Program
    <STAThread>
    Public Sub Main()
        Try
            Dim app As New Application()
            app.Run(New MainWindow())
        Catch ex As Exception
            MessageBox.Show("Critical Error: " & ex.Message)
        End Try
    End Sub
End Module

' --- Vault Connection Abstraction ---
Public Enum VaultConnectionMode
    Auto = 0
    WebApiOnly = 1
    LocalClientOnly = 2
End Enum

Public Class VaultComponentUnavailableException
    Inherits Exception

    Public Sub New(message As String)
        MyBase.New(message)
    End Sub

    Public Sub New(message As String, innerException As Exception)
        MyBase.New(message, innerException)
    End Sub
End Class

Public Class VaultFileInfo
    Public Property Id As Long
    Public Property Name As String = ""
    Public Property State As String = ""
    Public Property Revision As String = ""
    Public Property Size As Long = 0

    Public ReadOnly Property IsReleased As Boolean
        Get
            Return String.Equals(State, "Released", StringComparison.OrdinalIgnoreCase)
        End Get
    End Property
End Class

Public Interface IVaultBackend
    ReadOnly Property ModeName As String
    ReadOnly Property ShortBadge As String
    ReadOnly Property ConnectedUser As String
    ReadOnly Property ServerAddress As String
    ReadOnly Property IsConnected As Boolean
    ReadOnly Property UsedFallback As Boolean

    Function SearchFilesByName(term As String, Optional exactMatch As Boolean = False) As List(Of VaultFileInfo)
    Function GetFileDetails(fileId As Long) As VaultFileInfo
    Function GetDirectAttachments(fileId As Long) As List(Of VaultFileInfo)
    Function GetParentDependencies(fileId As Long) As List(Of VaultFileInfo)
    Function DownloadFile(fileId As Long, fileName As String, destDir As String, Optional forTempParse As Boolean = False) As String
    Sub Disconnect()
End Interface

Public Class VaultBackendFactory
    Public Shared Function IsLocalClientAvailable() As Boolean
        Return LocalClientVaultBackend.IsAvailable()
    End Function

    Public Shared Function NormalizeHostForLocal(serverInput As String) As String
        Dim raw = If(serverInput, "").Trim()
        If String.IsNullOrEmpty(raw) Then Throw New ArgumentException("Vault server address is required.")

        If raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) OrElse
           raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase) Then
            Try
                Dim u As New Uri(raw)
                Return u.Host
            Catch
            End Try
        End If

        Dim slashIdx = raw.IndexOf("/"c)
        If slashIdx > 0 Then raw = raw.Substring(0, slashIdx)
        Dim colonIdx = raw.IndexOf(":"c)
        If colonIdx > 0 Then raw = raw.Substring(0, colonIdx)
        Return raw.Trim()
    End Function

    Public Shared Function NormalizeOriginForWeb(serverInput As String) As String
        Dim raw = If(serverInput, "").Trim()
        If String.IsNullOrEmpty(raw) Then Throw New ArgumentException("Vault server address is required.")

        If Not raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) AndAlso
           Not raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase) Then
            raw = "http://" & raw
        End If

        Dim u As New Uri(raw)
        Return u.GetLeftPart(UriPartial.Authority).TrimEnd("/"c)
    End Function

    Public Shared Function Connect(mode As VaultConnectionMode,
                                   server As String,
                                   vaultName As String,
                                   user As String,
                                   pass As String,
                                   useWindowsAuth As Boolean,
                                   Optional forceLocalUnavailable As Boolean = False) As IVaultBackend
        Select Case mode
            Case VaultConnectionMode.WebApiOnly
                Return WebApiVaultBackend.Connect(server, vaultName, user, pass, useWindowsAuth, usedFallback:=False)

            Case VaultConnectionMode.LocalClientOnly
                Return LocalClientVaultBackend.Connect(server, vaultName, user, pass, useWindowsAuth)

            Case Else ' Auto
                If Not forceLocalUnavailable AndAlso LocalClientVaultBackend.IsAvailable() Then
                    Try
                        Return LocalClientVaultBackend.Connect(server, vaultName, user, pass, useWindowsAuth)
                    Catch ex As VaultComponentUnavailableException
                        Return WebApiVaultBackend.Connect(server, vaultName, user, pass, useWindowsAuth, usedFallback:=True)
                    End Try
                End If
                Return WebApiVaultBackend.Connect(server, vaultName, user, pass, useWindowsAuth, usedFallback:=True)
        End Select
    End Function
End Class

Public Class SearchProgressInfo
    Public Property Title As String = Nothing
    Public Property Detail As String = Nothing
    Public Property LogLine As String = Nothing
    Public Property CompletedParts As Integer = -1
    Public Property TotalParts As Integer = -1
    Public Property PdfsFound As Integer = -1
    Public Property MissingCount As Integer = -1
    Public Property IsIndeterminate As Nullable(Of Boolean) = Nothing
End Class

Public Class SearchSlot
    Public Property RowGrid As Grid
    Public Property TextBox As TextBox
    Public Property GoButton As Button
    Public Property RemoveButton As Button
    Public Property SearchTerm As String = ""
    Public Property SelectedFile As VaultFileInfo = Nothing
    Public Property RootItem As PrintItem = Nothing
End Class

' Main Logic
Public Class MainWindow
    Inherits Window

    ' --- Controls ---
    Private WithEvents btnLogin As Button
    Private WithEvents btnLogout As Button
    Private WithEvents btnSearch As Button
    Private btnRemoveFirst As Button
    Private WithEvents btnExportZip As Button
    Private WithEvents btnExportText As Button
    Private WithEvents btnExportMissing As Button
    Private WithEvents btnPrint As Button
    Private WithEvents btnToggleView As Button
    Private WithEvents btnToggleFilter As Button
    Private WithEvents btnAddSearch As Button
    Private WithEvents btnClearQueue As Button
    Private WithEvents btnToggleGuide As Button
    Private WithEvents btnCloseGuide As Button
    Private WithEvents chkShowGuideStartup As CheckBox
    Private WithEvents chkWinAuth As CheckBox
    Private WithEvents cmbMode As ComboBox
    Private pnlGuide As Border
    Private pnlEmptyQueueHint As Border
    Private brdModeBadge As Border
    Private lblModeBadge As TextBlock
    Private lblUserInfo As TextBlock
    Private lblEnvDetect As TextBlock
    Private chkIncludeStp As CheckBox
    Private chkIncludeZip As CheckBox
    Private chkIncludeDxf As CheckBox
    Private chkGroupSubfolders As CheckBox
    Private pnlSearchBoxes As StackPanel
    Private pnlSearchProgress As Grid
    Private spinnerArc As System.Windows.Shapes.Path
    Private _spinnerRotate As RotateTransform
    Private lblProgressTitle As TextBlock
    Private lblProgressMeta As TextBlock
    Private pbSearchProgress As ProgressBar
    Private lblProgressPercent As TextBlock
    Private lblProgressStep As TextBlock
    Private txtProgressLog As TextBox
    Private WithEvents btnCancelSearch As Button
    Private pbStatus As ProgressBar
    Private WithEvents treeFiles As TreeView
    Private WithEvents lstFiles As ListBox
    Private WithEvents txtSearch As TextBox
    Private lblStatus As TextBlock
    Private txtServer As TextBox
    Private txtUser As TextBox
    Private WithEvents txtPass As PasswordBox
    Private loginOverlay As Grid

    ' --- Backend ---
    Private vaultBackend As IVaultBackend = Nothing

    ' --- Data ---
    Private _flatList As New List(Of PrintItem)
    Private _processedIds As New HashSet(Of Long)
    Private _hideUnticked As Boolean = True
    Private _searchSlots As New List(Of SearchSlot)
    Private _lastSearchedSlot As SearchSlot = Nothing
    Private _preferLastSearchForExport As Boolean = True

    ' --- Search Progress & Cancellation ---
    Private _isSearching As Boolean = False
    Private _searchRunId As Integer = 0
    Private _searchCts As CancellationTokenSource = Nothing
    Private _searchStopwatch As New Stopwatch()
    Private _searchTimer As DispatcherTimer = Nothing
    Private _progressCompletedParts As Integer = 0
    Private _progressTotalParts As Integer = 0
    Private _progressPdfsFound As Integer = 0
    Private _progressMissingCount As Integer = 0

    Public ReadOnly Property SearchSlots As List(Of SearchSlot)
        Get
            Return _searchSlots
        End Get
    End Property

    Public Sub SetBackendForTest(backend As IVaultBackend)
        vaultBackend = backend
    End Sub

    Public Sub New()
        InitializeComponent()
    End Sub

    Private Sub InitializeComponent()
        ' --- 1. Define XAML (Modern Dark Theme) ---
        Dim xamlContent As XElement =
<Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid.Resources>
        <Style TargetType="Button">
            <Setter Property="Background" Value="#333"/>
            <Setter Property="Foreground" Value="White"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Padding" Value="10,5"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border Background="{TemplateBinding Background}" CornerRadius="4">
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                        </Border>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
            <Style.Triggers>
                <Trigger Property="IsMouseOver" Value="True">
                    <Setter Property="Background" Value="#444"/>
                </Trigger>
                <Trigger Property="IsEnabled" Value="False">
                    <Setter Property="Opacity" Value="0.45"/>
                </Trigger>
            </Style.Triggers>
        </Style>
        <Style TargetType="TextBox">
            <Setter Property="Background" Value="#252526"/>
            <Setter Property="Foreground" Value="White"/>
            <Setter Property="BorderBrush" Value="#3F3F46"/>
            <Setter Property="CaretBrush" Value="White"/>
            <Setter Property="Padding" Value="5"/>
            <Style.Triggers>
                <Trigger Property="IsEnabled" Value="False">
                    <Setter Property="Opacity" Value="0.45"/>
                </Trigger>
            </Style.Triggers>
        </Style>
        <Style TargetType="PasswordBox">
            <Setter Property="Background" Value="#252526"/>
            <Setter Property="Foreground" Value="White"/>
            <Setter Property="BorderBrush" Value="#3F3F46"/>
            <Setter Property="CaretBrush" Value="White"/>
            <Setter Property="Padding" Value="5"/>
            <Style.Triggers>
                <Trigger Property="IsEnabled" Value="False">
                    <Setter Property="Opacity" Value="0.45"/>
                </Trigger>
            </Style.Triggers>
        </Style>
        <Style TargetType="ComboBoxItem">
            <Setter Property="Background" Value="#252526"/>
            <Setter Property="Foreground" Value="White"/>
            <Setter Property="Padding" Value="8,6"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="ComboBoxItem">
                        <Border x:Name="Bd" Background="{TemplateBinding Background}" Padding="{TemplateBinding Padding}">
                            <ContentPresenter HorizontalAlignment="Left" VerticalAlignment="Center"/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsHighlighted" Value="True">
                                <Setter TargetName="Bd" Property="Background" Value="#007ACC"/>
                            </Trigger>
                            <Trigger Property="IsSelected" Value="True">
                                <Setter TargetName="Bd" Property="Background" Value="#173048"/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
        <Style TargetType="ComboBox">
            <Setter Property="Foreground" Value="White"/>
            <Setter Property="Background" Value="#252526"/>
            <Setter Property="BorderBrush" Value="#3F3F46"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="Padding" Value="6,5"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="ComboBox">
                        <Grid>
                            <ToggleButton x:Name="ToggleButton"
                                          Focusable="False"
                                          ClickMode="Press"
                                          IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}">
                                <ToggleButton.Template>
                                    <ControlTemplate TargetType="ToggleButton">
                                        <Border x:Name="Border" Background="#252526" BorderBrush="#3F3F46" BorderThickness="1" CornerRadius="3">
                                            <Grid>
                                                <Grid.ColumnDefinitions>
                                                    <ColumnDefinition Width="*"/>
                                                    <ColumnDefinition Width="24"/>
                                                </Grid.ColumnDefinitions>
                                                <TextBlock Grid.Column="1" Text="▾" Foreground="#AAA" HorizontalAlignment="Center" VerticalAlignment="Center" FontSize="11"/>
                                            </Grid>
                                        </Border>
                                        <ControlTemplate.Triggers>
                                            <Trigger Property="IsMouseOver" Value="True">
                                                <Setter TargetName="Border" Property="BorderBrush" Value="#007ACC"/>
                                            </Trigger>
                                        </ControlTemplate.Triggers>
                                    </ControlTemplate>
                                </ToggleButton.Template>
                            </ToggleButton>
                            <ContentPresenter x:Name="ContentSite"
                                              IsHitTestVisible="False"
                                              Content="{TemplateBinding SelectionBoxItem}"
                                              ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"
                                              Margin="8,5,26,5"
                                              VerticalAlignment="Center"
                                              HorizontalAlignment="Left"/>
                            <Popup x:Name="Popup"
                                   Placement="Bottom"
                                   IsOpen="{TemplateBinding IsDropDownOpen}"
                                   AllowsTransparency="True"
                                   Focusable="False"
                                   PopupAnimation="Slide">
                                <Grid x:Name="DropDown"
                                      SnapsToDevicePixels="True"
                                      MinWidth="{TemplateBinding ActualWidth}"
                                      MaxHeight="{TemplateBinding MaxDropDownHeight}">
                                    <Border x:Name="DropDownBorder" Background="#252526" BorderBrush="#007ACC" BorderThickness="1" CornerRadius="3">
                                        <ScrollViewer Margin="0" SnapsToDevicePixels="True">
                                            <StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Contained"/>
                                        </ScrollViewer>
                                    </Border>
                                </Grid>
                            </Popup>
                        </Grid>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </Grid.Resources>

    <Grid.RowDefinitions>
        <RowDefinition Height="Auto"/> <!-- Header -->
        <RowDefinition Height="*"/>    <!-- Content -->
        <RowDefinition Height="Auto"/> <!-- Footer -->
    </Grid.RowDefinitions>

    <!-- Header -->
    <Border Grid.Row="0" Background="#2D2D30" Padding="15">
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                <TextBlock Text="Vault Print Tool" FontWeight="Bold" Foreground="#DDD" FontSize="18" VerticalAlignment="Center"/>
                <Border Background="#3F3F46" CornerRadius="3" Padding="6,2" Margin="10,0,0,0" VerticalAlignment="Center">
                    <TextBlock Text="v3.0" Foreground="#AAA" FontSize="11" FontWeight="SemiBold"/>
                </Border>
                <Border x:Name="brdModeBadge" Background="#0E7490" BorderBrush="#17A2B8" BorderThickness="1" CornerRadius="3" Padding="8,2" Margin="8,0,0,0" VerticalAlignment="Center" Visibility="Collapsed">
                    <TextBlock x:Name="lblModeBadge" Text="Web API" Foreground="White" FontSize="11" FontWeight="SemiBold"/>
                </Border>
            </StackPanel>
            <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center">
                 <Button x:Name="btnToggleGuide" Content="📖 User Guide" Background="#333" Foreground="White" BorderThickness="1" BorderBrush="#555" Margin="0,0,15,0" Padding="10,5" ToolTip="Toggle Step-by-Step Instructions &amp; Drawing Status Guide"/>
                 <TextBlock x:Name="lblUserInfo" Text="Not Connected" Foreground="#AAA" VerticalAlignment="Center" Margin="0,0,10,0"/>
                 <Button x:Name="btnLogout" Content="Log Out" Visibility="Collapsed" Background="#C44" Padding="12,5" ToolTip="Disconnect current Vault session and return to login screen"/>
            </StackPanel>
        </Grid>
    </Border>

    <!-- Main Content -->
    <Grid Grid.Row="1" Margin="10">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="280"/>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="Auto"/>
        </Grid.ColumnDefinitions>

        <!-- Left Panel: Search -->
        <Border Grid.Column="0" Background="#252526" CornerRadius="4" Padding="10" Margin="0,0,10,0">
            <Grid>
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="*"/>
                </Grid.RowDefinitions>

                <StackPanel Orientation="Horizontal" Margin="0,0,0,5">
                    <TextBlock Text="Search Vault (IDW/IAM)" Foreground="#AAA" VerticalAlignment="Center" Margin="0,0,10,0"/>
                    <Button x:Name="btnAddSearch" Content="+" Padding="10,0" ToolTip="Add another search box to batch multiple assemblies" Background="#333" Foreground="White" BorderThickness="1" BorderBrush="#555"/>
                </StackPanel>

                <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto" MaxHeight="300">
                    <StackPanel x:Name="pnlSearchBoxes">
                        <Grid Margin="0,0,0,5">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*"/>
                                <ColumnDefinition Width="Auto"/>
                                <ColumnDefinition Width="Auto"/>
                            </Grid.ColumnDefinitions>
                            <TextBox x:Name="txtSearch" Margin="0,0,5,0" ToolTip="Enter drawing or assembly part number (e.g. 22000-1080) and press Enter or click Go"/>
                            <Button x:Name="btnSearch" Grid.Column="1" Content="Go" Background="#007ACC" ToolTip="Search Vault and automatically parse drawing BOM"/>
                            <Button x:Name="btnRemoveFirst" Content="X" Grid.Column="2" Background="#C44" Foreground="White" Margin="5,0,0,0" Width="20" Padding="2,5" ToolTip="Remove this assembly from the print queue"/>
                        </Grid>
                    </StackPanel>
                </ScrollViewer>

                <TextBlock Grid.Row="2" Text="Enter a drawing number above and click 'Go'." Foreground="#666" VerticalAlignment="Center" HorizontalAlignment="Center" TextWrapping="Wrap" Margin="15" TextAlignment="Center" FontSize="11"/>
            </Grid>
        </Border>

        <!-- Middle Panel: Tree View & Queue -->
        <Border Grid.Column="1" Background="#252526" CornerRadius="4" Padding="10">
            <Grid>
               <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="*"/>
                </Grid.RowDefinitions>

                <StackPanel Orientation="Horizontal" Margin="0,0,0,10">
                    <TextBlock Text="Print Queue" FontWeight="Bold" Foreground="White" FontSize="14" VerticalAlignment="Center"/>
                    <Button x:Name="btnClearQueue" Content="Clear" Margin="10,0,0,0" Padding="10,2" Background="#C44" Foreground="White" BorderThickness="0" VerticalAlignment="Center" ToolTip="Clear all loaded items from the print queue"/>
                    <TextBlock x:Name="lblSelectionMeta" Text="" Foreground="#AAA" Margin="10,0,0,0" VerticalAlignment="Center"/>
                    <Border Width="1" Background="#3F3F46" Margin="15,2" VerticalAlignment="Stretch"/>
                    <TextBlock Text="View:" Foreground="#888" VerticalAlignment="Center" Margin="0,0,5,0" FontSize="11"/>
                    <Button x:Name="btnToggleView" Content="📋 Switch to List View" Margin="0,0,10,0" Padding="10,2" Background="#333" Foreground="White" BorderThickness="1" BorderBrush="#555" ToolTip="Switch between hierarchical assembly tree view and flat list view (shows all drawings in a single sorted list)"/>
                    <TextBlock Text="Filter:" Foreground="#888" VerticalAlignment="Center" Margin="0,0,5,0" FontSize="11"/>
                    <Button x:Name="btnToggleFilter" Content="👁 Show Selected Only" Margin="0,0,0,0" Padding="10,2" Background="#333" Foreground="White" BorderThickness="1" BorderBrush="#555" ToolTip="Filter queue to show only checked drawings, or show all drawings"/>
                </StackPanel>

                <!-- Empty Queue Onboarding Placeholder -->
                <Border x:Name="pnlEmptyQueueHint" Grid.Row="1" Background="#1E1E1E" CornerRadius="6" BorderBrush="#3F3F46" BorderThickness="1" Margin="20" Padding="25" VerticalAlignment="Center" HorizontalAlignment="Center" MaxWidth="500">
                    <StackPanel HorizontalAlignment="Center">
                        <TextBlock Text="📋 Drawing Print Queue Empty" FontSize="17" FontWeight="Bold" Foreground="#007ACC" HorizontalAlignment="Center" Margin="0,0,0,10"/>
                        <TextBlock Text="Enter a drawing or assembly number (e.g. 22000-1080) on the left and click 'Go' to automatically parse the drawing BOM and resolve all drawings."
                                   Foreground="#CCC" TextWrapping="Wrap" TextAlignment="Center" FontSize="12" Margin="0,0,0,15" LineHeight="18"/>
                        <StackPanel Orientation="Horizontal" HorizontalAlignment="Center" Margin="0,0,0,15">
                            <Border Background="#2D2D30" CornerRadius="3" Padding="8,4" Margin="3">
                                <TextBlock Text="1. Search Part (pick *.idw)" Foreground="#AAA" FontSize="11"/>
                            </Border>
                            <TextBlock Text="➔" Foreground="#666" VerticalAlignment="Center" Margin="2,0"/>
                            <Border Background="#2D2D30" CornerRadius="3" Padding="8,4" Margin="3">
                                <TextBlock Text="2. Auto-Parse BOM" Foreground="#AAA" FontSize="11"/>
                            </Border>
                            <TextBlock Text="➔" Foreground="#666" VerticalAlignment="Center" Margin="2,0"/>
                            <Border Background="#2D2D30" CornerRadius="3" Padding="8,4" Margin="3">
                                <TextBlock Text="3. Export ZIP / Print" Foreground="#AAA" FontSize="11"/>
                            </Border>
                        </StackPanel>
                        <TextBlock Text="Need help? Click '📖 User Guide' in the top right header." Foreground="#888" FontSize="11" HorizontalAlignment="Center"/>
                    </StackPanel>
                </Border>

                <TreeView x:Name="treeFiles" Grid.Row="1" Background="Transparent" BorderThickness="0" Foreground="White">
                    <TreeView.Resources>
                        <SolidColorBrush x:Key="{x:Static SystemColors.HighlightBrushKey}" Color="#094771"/>
                        <SolidColorBrush x:Key="{x:Static SystemColors.HighlightTextBrushKey}" Color="White"/>
                        <SolidColorBrush x:Key="{x:Static SystemColors.InactiveSelectionHighlightBrushKey}" Color="#333338"/>
                        <SolidColorBrush x:Key="{x:Static SystemColors.InactiveSelectionHighlightTextBrushKey}" Color="White"/>
                    </TreeView.Resources>
                    <TreeView.ItemTemplate>
                        <HierarchicalDataTemplate ItemsSource="{Binding Children}">
                            <StackPanel Orientation="Horizontal" Margin="2">
                                <CheckBox IsChecked="{Binding IsChecked, Mode=TwoWay}" VerticalAlignment="Center" IsEnabled="{Binding IsEnabled}"/>
                                <TextBlock Text="{Binding Name}" Margin="5,0" VerticalAlignment="Center">
                                    <TextBlock.Style>
                                        <Style TargetType="TextBlock">
                                            <Setter Property="Foreground" Value="White" />
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding IsReleased}" Value="False">
                                                    <Setter Property="Foreground" Value="Orange" />
                                                </DataTrigger>
                                                <DataTrigger Binding="{Binding IsMissing}" Value="True">
                                                    <Setter Property="Foreground" Value="Salmon" />
                                                </DataTrigger>
                                                <DataTrigger Binding="{Binding IsDuplicate}" Value="True">
                                                    <Setter Property="Foreground" Value="Gray" />
                                                </DataTrigger>
                                                <DataTrigger Binding="{Binding IsPDF}" Value="False">
                                                    <Setter Property="Foreground" Value="LightBlue" />
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </TextBlock.Style>
                                </TextBlock>
                                <TextBlock Text="{Binding StatusText}" Foreground="#777" FontSize="10" VerticalAlignment="Center"/>
                            </StackPanel>
                        </HierarchicalDataTemplate>
                    </TreeView.ItemTemplate>
                    <TreeView.ItemContainerStyle>
                        <Style TargetType="TreeViewItem">
                            <Setter Property="IsExpanded" Value="{Binding IsExpanded}"/>
                            <Setter Property="Visibility" Value="{Binding UIVisibility}"/>
                        </Style>
                    </TreeView.ItemContainerStyle>
                </TreeView>

                <ListBox x:Name="lstFiles" Grid.Row="1" Background="Transparent" BorderThickness="0" Foreground="White" Visibility="Collapsed" ScrollViewer.HorizontalScrollBarVisibility="Disabled">
                    <ListBox.Resources>
                        <SolidColorBrush x:Key="{x:Static SystemColors.HighlightBrushKey}" Color="#094771"/>
                        <SolidColorBrush x:Key="{x:Static SystemColors.HighlightTextBrushKey}" Color="White"/>
                        <SolidColorBrush x:Key="{x:Static SystemColors.InactiveSelectionHighlightBrushKey}" Color="#333338"/>
                        <SolidColorBrush x:Key="{x:Static SystemColors.InactiveSelectionHighlightTextBrushKey}" Color="White"/>
                    </ListBox.Resources>
                    <ListBox.ItemContainerStyle>
                        <Style TargetType="ListBoxItem">
                            <Setter Property="Visibility" Value="{Binding UIVisibility}"/>
                        </Style>
                    </ListBox.ItemContainerStyle>
                    <ListBox.ItemTemplate>
                        <DataTemplate>
                            <StackPanel Orientation="Horizontal" Margin="2,4">
                                <CheckBox IsChecked="{Binding IsChecked, Mode=TwoWay}" VerticalAlignment="Center" IsEnabled="{Binding IsEnabled}" Margin="0,0,10,0"/>
                                <TextBlock Text="{Binding Name}" VerticalAlignment="Center" TextWrapping="Wrap" MaxWidth="500">
                                    <TextBlock.Style>
                                        <Style TargetType="TextBlock">
                                            <Setter Property="Foreground" Value="White" />
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding IsReleased}" Value="False">
                                                    <Setter Property="Foreground" Value="Orange" />
                                                </DataTrigger>
                                                <DataTrigger Binding="{Binding IsMissing}" Value="True">
                                                    <Setter Property="Foreground" Value="Salmon" />
                                                </DataTrigger>
                                                <DataTrigger Binding="{Binding IsDuplicate}" Value="True">
                                                    <Setter Property="Foreground" Value="Gray" />
                                                </DataTrigger>
                                                <DataTrigger Binding="{Binding IsPDF}" Value="False">
                                                    <Setter Property="Foreground" Value="LightBlue" />
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </TextBlock.Style>
                                </TextBlock>
                                <TextBlock Text="{Binding StatusText}" Foreground="#777" FontSize="10" VerticalAlignment="Center" Margin="10,0,0,0"/>
                            </StackPanel>
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>

                <!-- Search Progress Overlay -->
                <Grid x:Name="pnlSearchProgress" Grid.Row="1" Background="#D91E1E1E" Visibility="Collapsed">
                    <Border Background="#1E1E1E" BorderBrush="#007ACC" BorderThickness="1" CornerRadius="8" Padding="22,18" MaxWidth="580" MinWidth="420" HorizontalAlignment="Center" VerticalAlignment="Center" Margin="16">
                        <Grid>
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="Auto"/>
                            </Grid.RowDefinitions>

                            <!-- Header: Spinner + Title + Meta -->
                            <Grid Grid.Row="0" Margin="0,0,0,12">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="Auto"/>
                                    <ColumnDefinition Width="*"/>
                                </Grid.ColumnDefinitions>
                                <Grid Width="28" Height="28" Margin="0,0,12,0" VerticalAlignment="Center">
                                    <Ellipse Width="24" Height="24" Stroke="#3A3A40" StrokeThickness="3"/>
                                    <Path x:Name="spinnerArc" Width="24" Height="24" Stroke="#007ACC" StrokeThickness="3" StrokeStartLineCap="Round" StrokeEndLineCap="Round" Data="M 12,1.5 A 10.5,10.5 0 0 1 21.09,17.25" RenderTransformOrigin="0.5,0.5"/>
                                </Grid>
                                <StackPanel Grid.Column="1" VerticalAlignment="Center">
                                    <TextBlock x:Name="lblProgressTitle" Text="Searching Vault..." FontSize="15" FontWeight="SemiBold" Foreground="White" TextTrimming="CharacterEllipsis"/>
                                    <TextBlock x:Name="lblProgressMeta" Text="Elapsed: 00:00" FontSize="11" Foreground="#9CDCFE" Margin="0,2,0,0"/>
                                </StackPanel>
                            </Grid>

                            <!-- Progress Bar + Percent -->
                            <Grid Grid.Row="1" Margin="0,0,0,10">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*"/>
                                    <ColumnDefinition Width="Auto"/>
                                </Grid.ColumnDefinitions>
                                <ProgressBar x:Name="pbSearchProgress" Height="10" IsIndeterminate="True" Minimum="0" Maximum="100" Value="0" Background="#2D2D30" BorderBrush="#3F3F46" BorderThickness="1" Foreground="#007ACC" VerticalAlignment="Center"/>
                                <TextBlock x:Name="lblProgressPercent" Grid.Column="1" Text="Working..." Foreground="#CCC" FontSize="11" Margin="10,0,0,0" VerticalAlignment="Center"/>
                            </Grid>

                            <!-- Current Step Highlight -->
                            <Border Grid.Row="2" Background="#252526" BorderBrush="#38383E" BorderThickness="1" CornerRadius="4" Padding="10,6" Margin="0,0,0,10">
                                <TextBlock x:Name="lblProgressStep" Text="Contacting Vault..." Foreground="#EEE" FontSize="12" TextTrimming="CharacterEllipsis"/>
                            </Border>

                            <!-- Live Activity Output -->
                            <StackPanel Grid.Row="3" Margin="0,0,0,12">
                                <TextBlock Text="Live Activity Output" Foreground="#999" FontSize="11" FontWeight="SemiBold" Margin="0,0,0,4"/>
                                <TextBox x:Name="txtProgressLog" Height="185" IsReadOnly="True" Background="#141416" Foreground="#C8D0D8" BorderBrush="#38383E" BorderThickness="1" FontFamily="Consolas" FontSize="10.5" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto" TextWrapping="NoWrap" Padding="6"/>
                            </StackPanel>

                            <!-- Bottom Helper + Cancel -->
                            <Grid Grid.Row="4">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*"/>
                                    <ColumnDefinition Width="Auto"/>
                                </Grid.ColumnDefinitions>
                                <TextBlock Text="Vault searches and PDF BOM extraction run in the background." Foreground="#777" FontSize="11" VerticalAlignment="Center" Margin="0,0,12,0" TextWrapping="Wrap"/>
                                <Button x:Name="btnCancelSearch" Grid.Column="1" Content="Cancel Search" Width="115" Height="30" Background="#C44" Foreground="White"/>
                            </Grid>
                        </Grid>
                    </Border>
                </Grid>
            </Grid>
        </Border>

        <!-- Right Drawer: User Guide & Drawing Reference -->
        <Border x:Name="pnlGuide" Grid.Column="2" Background="#252526" CornerRadius="4" Padding="12" Margin="10,0,0,0" Width="430">
            <Grid>
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="*"/>
                    <RowDefinition Height="Auto"/>
                </Grid.RowDefinitions>

                <!-- Guide Header -->
                <Grid Grid.Row="0" Margin="0,0,0,8">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                        <TextBlock Text="📖 User Guide &amp; Workflow" FontWeight="Bold" Foreground="White" FontSize="14" VerticalAlignment="Center"/>
                    </StackPanel>
                    <Button x:Name="btnCloseGuide" Grid.Column="1" Content="✕" Width="24" Height="24" Padding="0" Background="#3A3A3D" Foreground="#DDD" FontWeight="Bold" ToolTip="Dismiss Guide (reopen anytime via 'User Guide' in top bar)"/>
                </Grid>

                <!-- Scrollable Guide Content -->
                <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto" Margin="0,0,0,8" Padding="0,0,4,0">
                    <StackPanel>
                        <!-- Quick Start Card -->
                        <Border Background="#1E1E1E" CornerRadius="4" Padding="9,7" Margin="0,0,0,7" BorderBrush="#3F3F46" BorderThickness="1">
                            <StackPanel>
                                <TextBlock Text="🚀 Quick Start (1-2-3)" FontWeight="Bold" Foreground="#007ACC" FontSize="12" Margin="0,0,0,4"/>
                                <TextBlock Text="1. Search &amp; Pick Drawing (Pick *.idw)" FontWeight="SemiBold" Foreground="#EEE" FontSize="11" Margin="0,1,0,1"/>
                                <TextBlock Text="Type a part or GA number (e.g. 22000-1080) and click 'Go' (or press Enter). Each search box manages one assembly: click '+' to add another assembly, replace text in a box to swap it, or click the red 'X' to remove that assembly from the queue." Foreground="#AAA" FontSize="10" TextWrapping="Wrap" Margin="0,0,0,4" LineHeight="14"/>

                                <TextBlock Text="2. Pure PDF BOM Extraction" FontWeight="SemiBold" Foreground="#EEE" FontSize="11" Margin="0,1,0,1"/>
                                <TextBlock Text="The tool automatically reads the drawing's Parts List table from Vault, recursing through all sub-assemblies (.iam) and components." Foreground="#AAA" FontSize="10" TextWrapping="Wrap" Margin="0,0,0,4" LineHeight="14"/>

                                <TextBlock Text="3. Review Queue &amp; Export" FontWeight="SemiBold" Foreground="#EEE" FontSize="11" Margin="0,1,0,1"/>
                                <TextBlock Text="Check or uncheck drawings as needed — unticking a sub-assembly unticks all parts below it. Click 'Export to ZIP' to package production drawings, or 'Print Selected' to print directly." Foreground="#AAA" FontSize="10" TextWrapping="Wrap" Margin="0,0,0,4" LineHeight="14"/>

                                <TextBlock Text="v3.0 Connection Modes (Zero-Install Web API)" FontWeight="SemiBold" Foreground="#17A2B8" FontSize="11" Margin="0,1,0,1"/>
                                <TextBlock Text="Auto mode uses the local Autodesk Vault Client 2025 if installed, and automatically falls back to Vault's built-in HTTP Web API when Vault Client is absent — no DLLs or Vault installation required. You can also force Web API Only or Windows Authentication on the login screen." Foreground="#AAA" FontSize="10" TextWrapping="Wrap" LineHeight="14"/>
                            </StackPanel>
                        </Border>

                        <!-- Queue Views & Filter Controls Card -->
                        <Border Background="#1E1E1E" CornerRadius="4" Padding="9,7" Margin="0,0,0,7" BorderBrush="#3F3F46" BorderThickness="1">
                            <StackPanel>
                                <TextBlock Text="🔀 Queue Views &amp; Filter Controls" FontWeight="Bold" Foreground="#007ACC" FontSize="12" Margin="0,0,0,4"/>

                                <TextBlock Text="• 'Switch to List View' / 'Switch to Tree View'" FontWeight="SemiBold" Foreground="#EEE" FontSize="11" Margin="0,1,0,1"/>
                                <TextBlock Text="Tree View displays the hierarchical assembly BOM structure (parents &amp; sub-assemblies). List View flattens everything into a single sorted list (checked drawings first, then alphabetical) — ideal for rapid scanning and seeing total drawing counts." Foreground="#AAA" FontSize="10" TextWrapping="Wrap" Margin="0,0,0,5" LineHeight="14"/>

                                <TextBlock Text="• 'Show Selected Only' / 'Show All Files'" FontWeight="SemiBold" Foreground="#EEE" FontSize="11" Margin="0,1,0,1"/>
                                <TextBlock Text="By default, all drawings are displayed. Clicking 'Show Selected Only' hides unticked items so you can review exactly what is queued for export or printing. Clicking 'Show All Files' reveals every drawing again." Foreground="#AAA" FontSize="10" TextWrapping="Wrap" LineHeight="14"/>
                            </StackPanel>
                        </Border>

                        <!-- Drawing Status & Legend Card -->
                        <Border Background="#1E1E1E" CornerRadius="4" Padding="9,7" Margin="0,0,0,7" BorderBrush="#3F3F46" BorderThickness="1">
                            <StackPanel>
                                <TextBlock Text="🏷️ Drawing Status &amp; Legend" FontWeight="Bold" Foreground="#007ACC" FontSize="12" Margin="0,0,0,4"/>

                                <Grid Margin="0,1,0,2">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="14"/>
                                        <ColumnDefinition Width="*"/>
                                    </Grid.ColumnDefinitions>
                                    <TextBlock Text="●" Foreground="White" FontSize="11" VerticalAlignment="Center"/>
                                    <TextBlock Grid.Column="1" Text="White (Released): Official drawing in Vault. Checked by default." Foreground="#BBB" FontSize="10" TextWrapping="Wrap"/>
                                </Grid>

                                <Grid Margin="0,1,0,2">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="14"/>
                                        <ColumnDefinition Width="*"/>
                                    </Grid.ColumnDefinitions>
                                    <TextBlock Text="●" Foreground="Orange" FontSize="11" VerticalAlignment="Center"/>
                                    <TextBlock Grid.Column="1" Text="Orange (WIP): Work-in-progress / unreleased drawing. Review before use." Foreground="#BBB" FontSize="10" TextWrapping="Wrap"/>
                                </Grid>

                                <Grid Margin="0,1,0,2">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="14"/>
                                        <ColumnDefinition Width="*"/>
                                    </Grid.ColumnDefinitions>
                                    <TextBlock Text="●" Foreground="Salmon" FontSize="11" VerticalAlignment="Center"/>
                                    <TextBlock Grid.Column="1" Text="Salmon (Missing PDF): Part in BOM, but no released PDF found in Vault." Foreground="#BBB" FontSize="10" TextWrapping="Wrap"/>
                                </Grid>

                                <Grid Margin="0,1,0,2">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="14"/>
                                        <ColumnDefinition Width="*"/>
                                    </Grid.ColumnDefinitions>
                                    <TextBlock Text="●" Foreground="LightBlue" FontSize="11" VerticalAlignment="Center"/>
                                    <TextBlock Grid.Column="1" Text="Light Blue (Source CAD): Autodesk Inventor 3D model (.iam / .ipt)." Foreground="#BBB" FontSize="10" TextWrapping="Wrap"/>
                                </Grid>

                                <Grid Margin="0,1,0,2">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="14"/>
                                        <ColumnDefinition Width="*"/>
                                    </Grid.ColumnDefinitions>
                                    <TextBlock Text="●" Foreground="Gray" FontSize="11" VerticalAlignment="Center"/>
                                    <TextBlock Grid.Column="1" Text="Gray (Duplicate): In multiple assemblies; unchecked to prevent double prints." Foreground="#BBB" FontSize="10" TextWrapping="Wrap"/>
                                </Grid>

                                <TextBlock Text="💡 Note: -1000 General Arrangements default to unchecked." Foreground="#007ACC" FontSize="10" TextWrapping="Wrap" Margin="0,3,0,0"/>
                            </StackPanel>
                        </Border>

                        <!-- Export & Output Options Card -->
                        <Border Background="#1E1E1E" CornerRadius="4" Padding="9,7" BorderBrush="#3F3F46" BorderThickness="1">
                            <StackPanel>
                                <TextBlock Text="📦 Export &amp; Print Features" FontWeight="Bold" Foreground="#007ACC" FontSize="12" Margin="0,0,0,4"/>
                                <TextBlock Text="• Export to ZIP: Bundles selected drawings named '&lt;Part&gt; Rev &lt;Rev&gt;.zip' (purges Vault cache folders)." Foreground="#AAA" FontSize="10" TextWrapping="Wrap" Margin="0,0,0,2"/>
                                <TextBlock Text="• Group Subfolders: Creates nested subfolders within the ZIP archive per sub-assembly." Foreground="#AAA" FontSize="10" TextWrapping="Wrap" Margin="0,0,0,2"/>
                                <TextBlock Text="• Also Export .stp / .zip / .dxf: Auto-fetches companion files already stored in Vault (.stp models, .dxf flat patterns, and .zip archives bundling multi-sheet .idw DXF exports)." Foreground="#AAA" FontSize="10" TextWrapping="Wrap" Margin="0,0,0,2"/>
                                <TextBlock Text="• Export List / Missing: Generates drawing manifest or missing PDF report for the drawing office." Foreground="#AAA" FontSize="10" TextWrapping="Wrap" Margin="0,0,0,2"/>
                                <TextBlock Text="• Print Selected: Dispatches checked drawings directly to your chosen printer." Foreground="#AAA" FontSize="10" TextWrapping="Wrap"/>
                            </StackPanel>
                        </Border>
                    </StackPanel>
                </ScrollViewer>

                <!-- Guide Footer / Preferences -->
                <Border Grid.Row="2" BorderBrush="#3F3F46" BorderThickness="0,1,0,0" Padding="0,8,0,0">
                    <StackPanel>
                        <CheckBox x:Name="chkShowGuideStartup" Content="Show guide automatically on startup" Foreground="#AAA" FontSize="11" IsChecked="True"/>
                        <TextBlock Text="You can toggle this guide anytime via '📖 User Guide' in the header." Foreground="#666" FontSize="10" Margin="0,3,0,0" TextWrapping="Wrap"/>
                    </StackPanel>
                </Border>
            </Grid>
        </Border>
    </Grid>

    <!-- Footer: Actions -->
    <Border Grid.Row="2" Background="#2D2D30" Padding="10">
        <Grid>
            <Grid.ColumnDefinitions>
                 <ColumnDefinition Width="*"/>
                 <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>

            <Grid Grid.Column="0" VerticalAlignment="Center" Margin="0,0,12,0">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>
                <ProgressBar x:Name="pbStatus" Width="110" Height="10" Margin="0,0,10,0" IsIndeterminate="True" Visibility="Collapsed" Background="#1E1E1E" BorderBrush="#3F3F46" BorderThickness="1" Foreground="#007ACC" VerticalAlignment="Center"/>
                <TextBlock x:Name="lblStatus" Grid.Column="1" Text="Ready" Foreground="#AAA" VerticalAlignment="Center" TextTrimming="CharacterEllipsis"/>
            </Grid>

            <StackPanel Grid.Column="1" Orientation="Horizontal">
                <StackPanel Orientation="Horizontal" Margin="0,0,15,0" VerticalAlignment="Center">
                    <CheckBox x:Name="chkGroupSubfolders" Content="Group Subfolders" Foreground="White" Margin="0,0,15,0" VerticalAlignment="Center" ToolTip="Create subfolders inside the ZIP archive for each sub-assembly"/>
                    <TextBlock Text="Also export:" Foreground="#AAA" Margin="0,0,5,0" VerticalAlignment="Center"/>
                    <CheckBox x:Name="chkIncludeStp" Content=".stp" Foreground="White" Margin="0,0,5,0" VerticalAlignment="Center" ToolTip="Automatically search Vault and include matching .stp 3D CAD models stored in Vault"/>
                    <CheckBox x:Name="chkIncludeZip" Content=".zip" Foreground="White" Margin="0,0,5,0" VerticalAlignment="Center" ToolTip="Automatically search Vault and include matching .zip files (multi-sheet .idw DXF export archives) stored in Vault"/>
                    <CheckBox x:Name="chkIncludeDxf" Content=".dxf" Foreground="White" Margin="0,0,5,0" VerticalAlignment="Center" ToolTip="Automatically search Vault and include matching .dxf flat-pattern profiles stored in Vault"/>
                </StackPanel>
                <Button x:Name="btnExportZip" Content="Export to ZIP" Background="#007ACC" FontSize="14" Padding="20,5" Margin="0,0,10,0" IsEnabled="False" ToolTip="Package selected drawings and attachments into a clean ZIP archive"/>
                <Button x:Name="btnExportText" Content="Export List" Background="#17A2B8" FontSize="14" Padding="20,5" Margin="0,0,10,0" IsEnabled="False" ToolTip="Export drawing list and BOM structure to a Text (.txt) or CSV file"/>
                <Button x:Name="btnExportMissing" Content="Export Missing" Background="#DC3545" FontSize="14" Padding="20,5" Margin="0,0,10,0" IsEnabled="False" ToolTip="Export a report of part numbers whose PDF drawings were not found in Vault"/>
                <Button x:Name="btnPrint" Content="Print Selected" Background="#009933" FontSize="14" Padding="20,5" IsEnabled="False" ToolTip="Send all checked PDF drawings directly to a printer"/>
            </StackPanel>
        </Grid>
    </Border>

    <!-- Overlay: Login -->
    <Grid x:Name="loginOverlay" Background="#AA000000" Grid.RowSpan="3" Visibility="Visible">
        <Border Background="#1E1E1E" Width="400" Padding="22" VerticalAlignment="Center" CornerRadius="8" BorderBrush="#007ACC" BorderThickness="1">
            <StackPanel>
                <TextBlock Text="Vault Login" FontSize="20" FontWeight="SemiBold" Foreground="White" Margin="0,0,0,4" HorizontalAlignment="Center"/>
                <TextBlock x:Name="lblEnvDetect" Text="Checking local Vault Client..." FontSize="11" Foreground="#888" Margin="0,0,0,16" HorizontalAlignment="Center" TextWrapping="Wrap" TextAlignment="Center"/>

                <TextBlock Text="Connection Mode" Foreground="#AAA" FontSize="12"/>
                <ComboBox x:Name="cmbMode" Margin="0,5,0,10" SelectedIndex="0" ToolTip="Auto uses Local Vault Client 2025 when installed and falls back to Vault Web API when absent">
                    <ComboBoxItem Content="Auto (Local Client → Web API Fallback)"/>
                    <ComboBoxItem Content="Web API Only (No Vault Client Required)"/>
                    <ComboBoxItem Content="Local Vault Client Only (2025 DLLs)"/>
                </ComboBox>

                <TextBlock Text="Server" Foreground="#AAA" FontSize="12"/>
                <TextBox x:Name="txtServer" Text="" Margin="0,5,0,10" ToolTip="Server hostname or URL (e.g. vault.company.local, vault, or http://vault)"/>

                <CheckBox x:Name="chkWinAuth" Content="Use Windows Authentication" Foreground="#CCC" Margin="0,0,0,12" ToolTip="Sign in using your current Windows domain account instead of a Vault username and password"/>

                <TextBlock Text="User" Foreground="#AAA" FontSize="12"/>
                <TextBox x:Name="txtUser" Text="" Margin="0,5,0,10"/>

                <TextBlock Text="Password" Foreground="#AAA" FontSize="12"/>
                <PasswordBox x:Name="txtPass" Margin="0,5,0,20"/>

                <Button x:Name="btnLogin" Content="Connect" Background="#007ACC" Height="36" FontWeight="SemiBold"/>
            </StackPanel>
        </Border>
    </Grid>

</Grid>

        ' --- 2. Load XAML ---
        Me.Title = "Vault Print Tool v3.0"
        Dim wa = SystemParameters.WorkArea
        Dim targetW As Double = 1480
        Dim targetH As Double = 940
        If wa.Width > 100 Then
            targetW = Math.Max(1000, Math.Min(1480, wa.Width - 40))
        End If
        If wa.Height > 100 Then
            targetH = Math.Max(680, Math.Min(940, wa.Height - 50))
        End If
        Me.Width = targetW
        Me.Height = targetH
        Me.MinWidth = 1000
        Me.MinHeight = 650
        Me.Background = New SolidColorBrush(Color.FromRgb(30, 30, 30))
        Me.WindowStartupLocation = WindowStartupLocation.CenterScreen

        ' Parse XAML
        Dim reader = XmlReader.Create(New StringReader(xamlContent.ToString()))
        Dim root As FrameworkElement = CType(XamlReader.Load(reader), FrameworkElement)
        Me.Content = root

        ' --- 3. Bind Controls ---
        txtSearch = root.FindName("txtSearch")
        btnSearch = root.FindName("btnSearch")
        btnRemoveFirst = root.FindName("btnRemoveFirst")
        btnAddSearch = root.FindName("btnAddSearch")
        btnClearQueue = root.FindName("btnClearQueue")
        chkIncludeStp = root.FindName("chkIncludeStp")
        chkIncludeZip = root.FindName("chkIncludeZip")
        chkIncludeDxf = root.FindName("chkIncludeDxf")
        chkGroupSubfolders = root.FindName("chkGroupSubfolders")
        pnlSearchBoxes = root.FindName("pnlSearchBoxes")
        pnlSearchProgress = root.FindName("pnlSearchProgress")
        spinnerArc = root.FindName("spinnerArc")
        If spinnerArc IsNot Nothing Then
            _spinnerRotate = New RotateTransform(0)
            spinnerArc.RenderTransform = _spinnerRotate
        End If
        lblProgressTitle = root.FindName("lblProgressTitle")
        lblProgressMeta = root.FindName("lblProgressMeta")
        pbSearchProgress = root.FindName("pbSearchProgress")
        lblProgressPercent = root.FindName("lblProgressPercent")
        lblProgressStep = root.FindName("lblProgressStep")
        txtProgressLog = root.FindName("txtProgressLog")
        btnCancelSearch = root.FindName("btnCancelSearch")
        pbStatus = root.FindName("pbStatus")
        btnExportZip = root.FindName("btnExportZip")
        btnExportText = root.FindName("btnExportText")
        btnExportMissing = root.FindName("btnExportMissing")
        btnPrint = root.FindName("btnPrint")
        treeFiles = root.FindName("treeFiles")
        lstFiles = root.FindName("lstFiles")
        btnToggleView = root.FindName("btnToggleView")
        btnToggleFilter = root.FindName("btnToggleFilter")
        lblStatus = root.FindName("lblStatus")

        _searchTimer = New DispatcherTimer() With {.Interval = TimeSpan.FromMilliseconds(250)}
        AddHandler _searchTimer.Tick, AddressOf SearchTimer_Tick

        Dim firstSlot As New SearchSlot() With {
            .RowGrid = TryCast(txtSearch.Parent, Grid),
            .TextBox = txtSearch,
            .GoButton = btnSearch,
            .RemoveButton = btnRemoveFirst
        }
        _searchSlots.Add(firstSlot)
        If btnRemoveFirst IsNot Nothing Then
            AddHandler btnRemoveFirst.Click, Sub(s, ev)
                                                 RemoveSearchSlot(firstSlot)
                                             End Sub
        End If

        treeFiles.AddHandler(CheckBox.ClickEvent, New RoutedEventHandler(AddressOf ItemCheckBox_Click))
        lstFiles.AddHandler(CheckBox.ClickEvent, New RoutedEventHandler(AddressOf ItemCheckBox_Click))

        btnToggleGuide = root.FindName("btnToggleGuide")
        btnCloseGuide = root.FindName("btnCloseGuide")
        chkShowGuideStartup = root.FindName("chkShowGuideStartup")
        pnlGuide = root.FindName("pnlGuide")
        pnlEmptyQueueHint = root.FindName("pnlEmptyQueueHint")

        brdModeBadge = root.FindName("brdModeBadge")
        lblModeBadge = root.FindName("lblModeBadge")
        lblUserInfo = root.FindName("lblUserInfo")
        btnLogout = root.FindName("btnLogout")

        loginOverlay = root.FindName("loginOverlay")
        lblEnvDetect = root.FindName("lblEnvDetect")
        cmbMode = root.FindName("cmbMode")
        txtServer = root.FindName("txtServer")
        chkWinAuth = root.FindName("chkWinAuth")
        txtUser = root.FindName("txtUser")
        txtPass = root.FindName("txtPass")
        btnLogin = root.FindName("btnLogin")

        ' Detect local Vault Client availability
        UpdateEnvironmentDetectionLabel()

        ' Initialize Guide Visibility from user preference
        Dim guidePref As Boolean = GetGuidePreference()
        SetGuideVisible(guidePref)
        If chkShowGuideStartup IsNot Nothing Then
            chkShowGuideStartup.IsChecked = guidePref
        End If
    End Sub

    Private Sub UpdateEnvironmentDetectionLabel()
        If lblEnvDetect Is Nothing Then Return
        If VaultBackendFactory.IsLocalClientAvailable() Then
            lblEnvDetect.Text = "✓ Local Vault Client 2025 detected (Auto uses Local Client)"
            lblEnvDetect.Foreground = New SolidColorBrush(Color.FromRgb(&H6E, &HC7, &H7E))
        Else
            lblEnvDetect.Text = "ℹ No local Vault Client detected (Auto will use Web API)"
            lblEnvDetect.Foreground = New SolidColorBrush(Color.FromRgb(&H4E, &HC9, &HB0))
        End If
    End Sub

    ' --- Guide & Help Management ---
    Private Sub BtnToggleGuide_Click(sender As Object, e As RoutedEventArgs) Handles btnToggleGuide.Click
        Dim isCurrentlyVisible As Boolean = (pnlGuide IsNot Nothing AndAlso pnlGuide.Visibility = Visibility.Visible)
        Dim newVis = Not isCurrentlyVisible
        SetGuideVisible(newVis)
        SaveGuidePreference(newVis)
        If chkShowGuideStartup IsNot Nothing Then
            chkShowGuideStartup.IsChecked = newVis
        End If
    End Sub

    Private Sub BtnCloseGuide_Click(sender As Object, e As RoutedEventArgs) Handles btnCloseGuide.Click
        SetGuideVisible(False)
        SaveGuidePreference(False)
        If chkShowGuideStartup IsNot Nothing Then
            chkShowGuideStartup.IsChecked = False
        End If
    End Sub

    Private Sub ChkShowGuideStartup_Click(sender As Object, e As RoutedEventArgs) Handles chkShowGuideStartup.Click
        Dim isChecked As Boolean = chkShowGuideStartup.IsChecked.GetValueOrDefault(True)
        SaveGuidePreference(isChecked)
    End Sub

    Private Sub SetGuideVisible(visible As Boolean)
        If pnlGuide IsNot Nothing Then
            pnlGuide.Visibility = If(visible, Visibility.Visible, Visibility.Collapsed)
        End If
        If btnToggleGuide IsNot Nothing Then
            If visible Then
                btnToggleGuide.Background = New SolidColorBrush(Color.FromRgb(0, 122, 204))
                btnToggleGuide.BorderBrush = New SolidColorBrush(Color.FromRgb(0, 122, 204))
            Else
                btnToggleGuide.Background = New SolidColorBrush(Color.FromRgb(&H33, &H33, &H33))
                btnToggleGuide.BorderBrush = New SolidColorBrush(Color.FromRgb(&H55, &H55, &H55))
            End If
        End If
    End Sub

    Private Shared Function GetGuidePreference() As Boolean
        Try
            Dim settingsFile As String = IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VaultPrintTool", "guidetoggle.txt")
            If IO.File.Exists(settingsFile) Then
                Dim val = IO.File.ReadAllText(settingsFile).Trim()
                If val.Equals("0", StringComparison.OrdinalIgnoreCase) OrElse val.Equals("false", StringComparison.OrdinalIgnoreCase) Then
                    Return False
                End If
            End If
        Catch
        End Try
        Return True
    End Function

    Private Shared Sub SaveGuidePreference(show As Boolean)
        Try
            Dim dir As String = IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VaultPrintTool")
            If Not IO.Directory.Exists(dir) Then IO.Directory.CreateDirectory(dir)
            Dim settingsFile As String = IO.Path.Combine(dir, "guidetoggle.txt")
            IO.File.WriteAllText(settingsFile, If(show, "1", "0"))
        Catch
        End Try
    End Sub

    ' --- Login / Logout / Lifecycle ---
    Private Sub ChkWinAuth_Changed(sender As Object, e As RoutedEventArgs) Handles chkWinAuth.Checked, chkWinAuth.Unchecked
        Dim useWinAuth As Boolean = chkWinAuth.IsChecked.GetValueOrDefault(False)
        If txtUser IsNot Nothing Then txtUser.IsEnabled = Not useWinAuth
        If txtPass IsNot Nothing Then txtPass.IsEnabled = Not useWinAuth
    End Sub

    Private Sub TxtPass_KeyDown(sender As Object, e As KeyEventArgs) Handles txtPass.KeyDown
        If e.Key = Key.Enter Then
            BtnLogin_Click(btnLogin, New RoutedEventArgs())
        End If
    End Sub

    Private Sub TxtSearch_KeyDown(sender As Object, e As KeyEventArgs) Handles txtSearch.KeyDown
        If e.Key = Key.Enter Then
            BtnSearch_Click(sender, New RoutedEventArgs())
        End If
    End Sub

    Private Sub BtnLogin_Click(sender As Object, e As RoutedEventArgs) Handles btnLogin.Click
        Dim srv = txtServer.Text.Trim()
        Dim usr = txtUser.Text.Trim()
        Dim pwd = txtPass.Password
        If String.IsNullOrWhiteSpace(srv) Then
            MessageBox.Show("Please enter a Vault server address.", "Vault Connection Error", MessageBoxButton.OK, MessageBoxImage.Warning)
            Return
        End If
        Dim useWinAuth As Boolean = chkWinAuth.IsChecked.GetValueOrDefault(False)
        Dim selectedMode As VaultConnectionMode = VaultConnectionMode.Auto
        If cmbMode IsNot Nothing AndAlso cmbMode.SelectedIndex >= 0 AndAlso cmbMode.SelectedIndex <= 2 Then
            selectedMode = CType(cmbMode.SelectedIndex, VaultConnectionMode)
        End If

        lblStatus.Text = "Connecting..."
        btnLogin.IsEnabled = False
        Me.Cursor = Cursors.Wait

        Application.Current.Dispatcher.Invoke(Sub()
            Try
                DisconnectCurrentBackend()

                Dim backend As IVaultBackend = VaultBackendFactory.Connect(selectedMode, srv, "Vault", usr, pwd, useWinAuth)
                If backend IsNot Nothing AndAlso backend.IsConnected Then
                    vaultBackend = backend
                    loginOverlay.Visibility = Visibility.Collapsed
                    lblUserInfo.Text = "User: " & backend.ConnectedUser
                    btnLogout.Visibility = Visibility.Visible

                    brdModeBadge.Visibility = Visibility.Visible
                    lblModeBadge.Text = backend.ShortBadge
                    brdModeBadge.ToolTip = backend.ModeName & vbCrLf & "Server: " & backend.ServerAddress

                    If backend.UsedFallback Then
                        brdModeBadge.Background = New SolidColorBrush(Color.FromRgb(&HB4, &H53, &H09))
                        brdModeBadge.BorderBrush = New SolidColorBrush(Color.FromRgb(&HF5, &H9E, &H0B))
                    ElseIf TypeOf backend Is WebApiVaultBackend Then
                        brdModeBadge.Background = New SolidColorBrush(Color.FromRgb(&H0E, &H74, &H90))
                        brdModeBadge.BorderBrush = New SolidColorBrush(Color.FromRgb(&H17, &HA2, &HB8))
                    Else
                        brdModeBadge.Background = New SolidColorBrush(Color.FromRgb(&H00, &H5A, &H9E))
                        brdModeBadge.BorderBrush = New SolidColorBrush(Color.FromRgb(&H00, &H7A, &HCC))
                    End If

                    lblStatus.Text = "Connected (" & backend.ModeName & ")."
                End If
            Catch ex As Exception
                MessageBox.Show("Login Failed: " & ex.Message, "Vault Connection Error", MessageBoxButton.OK, MessageBoxImage.Error)
                lblStatus.Text = "Login Failed."
            Finally
                btnLogin.IsEnabled = True
                Me.Cursor = Nothing
            End Try
        End Sub, System.Windows.Threading.DispatcherPriority.Background)
    End Sub

    Private Sub BtnLogout_Click(sender As Object, e As RoutedEventArgs) Handles btnLogout.Click
        If _isSearching Then Return
        DisconnectCurrentBackend()
        BtnClearQueue_Click(btnClearQueue, New RoutedEventArgs())
        lblUserInfo.Text = "Not Connected"
        btnLogout.Visibility = Visibility.Collapsed
        brdModeBadge.Visibility = Visibility.Collapsed
        UpdateEnvironmentDetectionLabel()
        loginOverlay.Visibility = Visibility.Visible
        lblStatus.Text = "Logged out."
    End Sub

    Private Sub MainWindow_Closing(sender As Object, e As System.ComponentModel.CancelEventArgs) Handles Me.Closing
        If _searchCts IsNot Nothing Then
            Try : _searchCts.Cancel() : Catch : End Try
        End If
        DisconnectCurrentBackend()
    End Sub

    Private Sub DisconnectCurrentBackend()
        If vaultBackend IsNot Nothing Then
            Try
                vaultBackend.Disconnect()
            Catch
            End Try
            vaultBackend = Nothing
        End If
    End Sub

    ' --- Search & Queue Handlers ---
    Private Function FindSlotForControl(ctrl As Object) As SearchSlot
        For Each slot In _searchSlots
            If Object.ReferenceEquals(slot.TextBox, ctrl) OrElse
               Object.ReferenceEquals(slot.GoButton, ctrl) OrElse
               Object.ReferenceEquals(slot.RemoveButton, ctrl) OrElse
               Object.ReferenceEquals(slot.RowGrid, ctrl) Then
                Return slot
            End If
        Next
        Dim fe As FrameworkElement = TryCast(ctrl, FrameworkElement)
        If fe IsNot Nothing Then
            Dim parentGrid = TryCast(fe.Parent, Grid)
            If parentGrid IsNot Nothing Then
                For Each slot In _searchSlots
                    If Object.ReferenceEquals(slot.RowGrid, parentGrid) Then Return slot
                Next
            End If
        End If
        If _searchSlots.Count > 0 Then Return _searchSlots(0)
        Return Nothing
    End Function

    Private Sub BtnAddSearch_Click(sender As Object, e As RoutedEventArgs) Handles btnAddSearch.Click
        If _isSearching Then Return
        AddSearchSlot()
    End Sub

    Public Function AddSearchSlot() As SearchSlot
        Dim grid As New Grid()
        grid.Margin = New Thickness(0, 0, 0, 5)

        Dim col1 As New ColumnDefinition() With {.Width = New GridLength(1, GridUnitType.Star)}
        Dim col2 As New ColumnDefinition() With {.Width = GridLength.Auto}
        Dim col3 As New ColumnDefinition() With {.Width = GridLength.Auto}
        grid.ColumnDefinitions.Add(col1)
        grid.ColumnDefinitions.Add(col2)
        grid.ColumnDefinitions.Add(col3)

        Dim txt As New TextBox()
        txt.Margin = New Thickness(0, 0, 5, 0)
        txt.ToolTip = "Enter drawing or assembly part number and press Enter or click Go"

        Dim tbStyle As New Style(GetType(TextBox))
        tbStyle.Setters.Add(New Setter(TextBox.BackgroundProperty, New SolidColorBrush(Color.FromRgb(&H25, &H25, &H26))))
        tbStyle.Setters.Add(New Setter(TextBox.ForegroundProperty, Brushes.White))
        tbStyle.Setters.Add(New Setter(TextBox.BorderBrushProperty, New SolidColorBrush(Color.FromRgb(&H3F, &H3F, &H46))))
        tbStyle.Setters.Add(New Setter(TextBox.CaretBrushProperty, Brushes.White))
        tbStyle.Setters.Add(New Setter(TextBox.PaddingProperty, New Thickness(5)))
        txt.Style = tbStyle

        Grid.SetColumn(txt, 0)
        grid.Children.Add(txt)

        Dim btn As New Button()
        btn.Content = "Go"
        btn.Background = New SolidColorBrush(Color.FromRgb(&H00, &H7A, &HCC))
        btn.Foreground = Brushes.White
        btn.Padding = New Thickness(10, 5, 10, 5)
        btn.BorderThickness = New Thickness(0)
        btn.ToolTip = "Search Vault and automatically parse drawing BOM"

        Grid.SetColumn(btn, 1)
        grid.Children.Add(btn)

        Dim btnRemove As New Button()
        btnRemove.Content = "X"
        btnRemove.Background = New SolidColorBrush(Color.FromRgb(&HC4, &H44, &H44))
        btnRemove.Foreground = Brushes.White
        btnRemove.Margin = New Thickness(5, 0, 0, 0)
        btnRemove.Width = 20
        btnRemove.Padding = New Thickness(2, 5, 2, 5)
        btnRemove.BorderThickness = New Thickness(0)
        btnRemove.ToolTip = "Remove this search box and its assembly from the print queue"

        Grid.SetColumn(btnRemove, 2)
        grid.Children.Add(btnRemove)

        Dim slot As New SearchSlot() With {
            .RowGrid = grid,
            .TextBox = txt,
            .GoButton = btn,
            .RemoveButton = btnRemove
        }

        AddHandler txt.KeyDown, Sub(s, ev)
                                    If ev.Key = Key.Enter Then
                                        ExecuteSlotSearch(slot)
                                    End If
                                End Sub
        AddHandler btn.Click, Sub(s, ev)
                                  ExecuteSlotSearch(slot)
                              End Sub
        AddHandler btnRemove.Click, Sub(s, ev)
                                        RemoveSearchSlot(slot)
                                    End Sub

        _searchSlots.Add(slot)
        pnlSearchBoxes.Children.Add(grid)
        Return slot
    End Function

    Public Sub RemoveSearchSlot(slot As SearchSlot)
        If _isSearching Then Return
        If slot Is Nothing Then Return

        If _searchSlots.Count > 1 Then
            If slot.RowGrid IsNot Nothing Then
                pnlSearchBoxes.Children.Remove(slot.RowGrid)
            End If
            _searchSlots.Remove(slot)
        Else
            If slot.TextBox IsNot Nothing Then slot.TextBox.Text = ""
            slot.SearchTerm = ""
            slot.SelectedFile = Nothing
            slot.RootItem = Nothing
        End If

        If Object.ReferenceEquals(_lastSearchedSlot, slot) OrElse
           (_lastSearchedSlot IsNot Nothing AndAlso _lastSearchedSlot.RootItem Is Nothing) Then
            _lastSearchedSlot = _searchSlots.LastOrDefault(Function(s) s.RootItem IsNot Nothing)
        End If
        _preferLastSearchForExport = True

        RebuildQueueFromSlots()
        If _flatList.Count = 0 Then
            lblStatus.Text = "Queue cleared."
        Else
            lblStatus.Text = "Removed assembly from queue."
        End If
    End Sub

    Private Sub BtnClearQueue_Click(sender As Object, e As RoutedEventArgs) Handles btnClearQueue.Click
        If _isSearching Then Return
        For i As Integer = _searchSlots.Count - 1 To 1 Step -1
            Dim extra = _searchSlots(i)
            If extra.RowGrid IsNot Nothing Then
                pnlSearchBoxes.Children.Remove(extra.RowGrid)
            End If
            _searchSlots.RemoveAt(i)
        Next
        If _searchSlots.Count > 0 Then
            Dim first = _searchSlots(0)
            If first.TextBox IsNot Nothing Then first.TextBox.Text = ""
            first.SearchTerm = ""
            first.SelectedFile = Nothing
            first.RootItem = Nothing
        End If
        _lastSearchedSlot = Nothing
        _preferLastSearchForExport = True

        RebuildQueueFromSlots()
        lblStatus.Text = "Queue cleared."
    End Sub

    Private Sub BtnSearch_Click(sender As Object, e As RoutedEventArgs) Handles btnSearch.Click
        Dim slot = FindSlotForControl(sender)
        ExecuteSlotSearch(slot)
    End Sub

    ' --- Search Progress UI Helpers ---
    Private Shared Function FormatElapsed(ts As TimeSpan) As String
        If ts.TotalHours >= 1 Then
            Return String.Format("{0}:{1:00}:{2:00}", CInt(Math.Floor(ts.TotalHours)), ts.Minutes, ts.Seconds)
        End If
        Return String.Format("{0:00}:{1:00}", ts.Minutes, ts.Seconds)
    End Function

    Private Shared Function FormatLogTimestamp(ts As TimeSpan) As String
        Return String.Format("[{0:00}:{1:00}.{2}]", CInt(Math.Floor(ts.TotalMinutes)), ts.Seconds, ts.Milliseconds \ 100)
    End Function

    Private Sub UpdateProgressMetaLabel()
        If lblProgressMeta Is Nothing Then Return
        Dim parts As New List(Of String)()
        parts.Add("Elapsed: " & FormatElapsed(_searchStopwatch.Elapsed))
        If _progressTotalParts > 0 Then
            parts.Add(String.Format("Parts: {0}/{1}", Math.Min(_progressCompletedParts, _progressTotalParts), _progressTotalParts))
        End If
        If _progressPdfsFound > 0 Then
            parts.Add(String.Format("PDFs found: {0}", _progressPdfsFound))
        End If
        If _progressMissingCount > 0 Then
            parts.Add(String.Format("Missing: {0}", _progressMissingCount))
        End If
        lblProgressMeta.Text = String.Join("  |  ", parts)
    End Sub

    Private Sub SearchTimer_Tick(sender As Object, e As EventArgs)
        If _isSearching Then
            UpdateProgressMetaLabel()
        End If
    End Sub

    Private Sub AppendSearchLog(message As String)
        If String.IsNullOrEmpty(message) Then Return
        Dim line = FormatLogTimestamp(_searchStopwatch.Elapsed) & " " & message
        If txtProgressLog IsNot Nothing Then
            txtProgressLog.AppendText(line & Environment.NewLine)
            txtProgressLog.ScrollToEnd()
        End If
    End Sub

    Private Function BeginSearchUi(slot As SearchSlot, term As String) As Integer
        _isSearching = True
        _searchRunId += 1
        If _searchCts IsNot Nothing Then
            Try : _searchCts.Dispose() : Catch : End Try
        End If
        _searchCts = New CancellationTokenSource()

        _progressCompletedParts = 0
        _progressTotalParts = 0
        _progressPdfsFound = 0
        _progressMissingCount = 0

        If txtProgressLog IsNot Nothing Then txtProgressLog.Clear()
        If lblProgressTitle IsNot Nothing Then lblProgressTitle.Text = "Searching Vault for '" & term & "'..."
        If lblProgressStep IsNot Nothing Then lblProgressStep.Text = "Contacting Vault and searching for matching files..."
        If lblProgressPercent IsNot Nothing Then lblProgressPercent.Text = "Working..."
        If pbSearchProgress IsNot Nothing Then
            pbSearchProgress.IsIndeterminate = True
            pbSearchProgress.Maximum = 100
            pbSearchProgress.Value = 0
        End If
        If pbStatus IsNot Nothing Then
            pbStatus.IsIndeterminate = True
            pbStatus.Maximum = 100
            pbStatus.Value = 0
            pbStatus.Visibility = Visibility.Visible
        End If
        If btnCancelSearch IsNot Nothing Then
            btnCancelSearch.IsEnabled = True
            btnCancelSearch.Content = "Cancel Search"
        End If

        _searchStopwatch.Restart()
        UpdateProgressMetaLabel()
        If _searchTimer IsNot Nothing Then _searchTimer.Start()

        If _spinnerRotate IsNot Nothing Then
            Dim anim As New DoubleAnimation(0, 360, New Duration(TimeSpan.FromSeconds(0.9)))
            anim.RepeatBehavior = RepeatBehavior.Forever
            _spinnerRotate.BeginAnimation(RotateTransform.AngleProperty, anim)
        End If

        If pnlSearchProgress IsNot Nothing Then pnlSearchProgress.Visibility = Visibility.Visible
        SetSearchUiBusy(True, slot)
        lblStatus.Text = "Searching Vault for " & term & "..."

        Dim modeDesc = If(vaultBackend IsNot Nothing AndAlso Not String.IsNullOrEmpty(vaultBackend.ModeName), vaultBackend.ModeName, "Vault")
        AppendSearchLog("Started search for '" & term & "' (" & modeDesc & ")...")

        Return _searchRunId
    End Function

    Private Sub EndSearchUi()
        If _searchTimer IsNot Nothing Then _searchTimer.Stop()
        _searchStopwatch.Stop()
        If _spinnerRotate IsNot Nothing Then
            _spinnerRotate.BeginAnimation(RotateTransform.AngleProperty, Nothing)
        End If
        If pnlSearchProgress IsNot Nothing Then pnlSearchProgress.Visibility = Visibility.Collapsed
        If pbStatus IsNot Nothing Then pbStatus.Visibility = Visibility.Collapsed
        If _searchCts IsNot Nothing Then
            Try : _searchCts.Dispose() : Catch : End Try
            _searchCts = Nothing
        End If
        _isSearching = False
        SetSearchUiBusy(False)
    End Sub

    Private Sub SetSearchUiBusy(isBusy As Boolean, Optional activeSlot As SearchSlot = Nothing)
        If btnAddSearch IsNot Nothing Then btnAddSearch.IsEnabled = Not isBusy
        If btnClearQueue IsNot Nothing Then btnClearQueue.IsEnabled = Not isBusy
        If btnLogout IsNot Nothing Then btnLogout.IsEnabled = Not isBusy

        For Each s In _searchSlots
            If s.TextBox IsNot Nothing Then s.TextBox.IsEnabled = Not isBusy
            If s.RemoveButton IsNot Nothing Then s.RemoveButton.IsEnabled = Not isBusy
            If s.GoButton IsNot Nothing Then
                s.GoButton.IsEnabled = Not isBusy
                If isBusy AndAlso Object.ReferenceEquals(s, activeSlot) Then
                    s.GoButton.Content = "..."
                Else
                    s.GoButton.Content = "Go"
                End If
            End If
        Next

        If isBusy Then
            If btnExportZip IsNot Nothing Then btnExportZip.IsEnabled = False
            If btnExportText IsNot Nothing Then btnExportText.IsEnabled = False
            If btnExportMissing IsNot Nothing Then btnExportMissing.IsEnabled = False
            If btnPrint IsNot Nothing Then btnPrint.IsEnabled = False
        Else
            UpdateActionButtonStates()
        End If
    End Sub

    Private Sub ReportSearchProgress(runId As Integer, info As SearchProgressInfo)
        If info Is Nothing Then Return
        If Me.Dispatcher.CheckAccess() Then
            ApplySearchProgress(runId, info)
        Else
            Me.Dispatcher.BeginInvoke(New Action(Sub() ApplySearchProgress(runId, info)), DispatcherPriority.Normal)
        End If
    End Sub

    Private Sub ApplySearchProgress(runId As Integer, info As SearchProgressInfo)
        If info Is Nothing OrElse runId <> _searchRunId OrElse Not _isSearching Then Return

        If Not String.IsNullOrEmpty(info.Title) AndAlso lblProgressTitle IsNot Nothing Then
            lblProgressTitle.Text = info.Title
        End If
        If Not String.IsNullOrEmpty(info.Detail) Then
            If lblProgressStep IsNot Nothing Then lblProgressStep.Text = info.Detail
            If lblStatus IsNot Nothing Then lblStatus.Text = info.Detail
        End If

        If info.CompletedParts >= 0 Then _progressCompletedParts = info.CompletedParts
        If info.TotalParts >= 0 Then _progressTotalParts = info.TotalParts
        If info.PdfsFound >= 0 Then _progressPdfsFound = info.PdfsFound
        If info.MissingCount >= 0 Then _progressMissingCount = info.MissingCount

        If pbSearchProgress IsNot Nothing Then
            If info.IsIndeterminate.HasValue AndAlso info.IsIndeterminate.Value Then
                pbSearchProgress.IsIndeterminate = True
                If lblProgressPercent IsNot Nothing Then lblProgressPercent.Text = "Working..."
            ElseIf _progressTotalParts > 0 AndAlso _progressCompletedParts > 0 Then
                Dim clampedDone = Math.Min(_progressCompletedParts, _progressTotalParts)
                Dim pct As Double = (CDbl(clampedDone) / CDbl(_progressTotalParts)) * 100.0
                pbSearchProgress.IsIndeterminate = False
                pbSearchProgress.Maximum = 100
                pbSearchProgress.Value = pct
                If lblProgressPercent IsNot Nothing Then
                    lblProgressPercent.Text = String.Format("{0}% ({1}/{2})", CInt(Math.Round(pct)), clampedDone, _progressTotalParts)
                End If
            Else
                pbSearchProgress.IsIndeterminate = True
                If lblProgressPercent IsNot Nothing Then
                    lblProgressPercent.Text = If(_progressTotalParts > 0, String.Format("0/{0} parts...", _progressTotalParts), "Working...")
                End If
            End If
            If pbStatus IsNot Nothing Then
                pbStatus.IsIndeterminate = pbSearchProgress.IsIndeterminate
                pbStatus.Value = pbSearchProgress.Value
            End If
        End If

        UpdateProgressMetaLabel()

        If Not String.IsNullOrEmpty(info.LogLine) Then
            AppendSearchLog(info.LogLine)
        End If
    End Sub

    Public Sub CancelCurrentSearch()
        If Not _isSearching OrElse _searchCts Is Nothing Then Return
        Try : _searchCts.Cancel() : Catch : End Try
        If btnCancelSearch IsNot Nothing Then
            btnCancelSearch.IsEnabled = False
            btnCancelSearch.Content = "Cancelling..."
        End If
        If lblProgressStep IsNot Nothing Then lblProgressStep.Text = "Cancelling current search..."
        If lblStatus IsNot Nothing Then lblStatus.Text = "Cancelling search..."
        AppendSearchLog("Cancellation requested by user...")
    End Sub

    Private Sub BtnCancelSearch_Click(sender As Object, e As RoutedEventArgs) Handles btnCancelSearch.Click
        CancelCurrentSearch()
    End Sub

    Private Shared Function SearchAndFilterMatches(backend As IVaultBackend, term As String, ct As CancellationToken) As List(Of VaultFileInfo)
        ct.ThrowIfCancellationRequested()
        Dim rawMatches = backend.SearchFilesByName(term, False)
        ct.ThrowIfCancellationRequested()

        If rawMatches Is Nothing OrElse rawMatches.Count = 0 Then
            Return New List(Of VaultFileInfo)()
        End If

        Dim validMatches As New List(Of VaultFileInfo)()
        Dim primaryMatches As New List(Of VaultFileInfo)()
        Dim seenIds As New HashSet(Of Long)()

        For Each f In rawMatches
            If f Is Nothing Then Continue For
            If f.Id > 0 AndAlso seenIds.Contains(f.Id) Then Continue For
            If f.Id > 0 Then seenIds.Add(f.Id)

            Dim n As String = If(String.IsNullOrEmpty(f.Name), "Unknown", f.Name)

            Dim isPrimary As Boolean = n.EndsWith(".idw", StringComparison.OrdinalIgnoreCase) OrElse
                                       n.EndsWith(".iam", StringComparison.OrdinalIgnoreCase) OrElse
                                       n.EndsWith(".ipt", StringComparison.OrdinalIgnoreCase)

            Dim isJunk As Boolean = n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) OrElse
                                    n.EndsWith(".stp", StringComparison.OrdinalIgnoreCase) OrElse
                                    n.EndsWith(".step", StringComparison.OrdinalIgnoreCase) OrElse
                                    n.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase) OrElse
                                    n.EndsWith(".dwf", StringComparison.OrdinalIgnoreCase)

            If Not isJunk Then
                validMatches.Add(f)
                If isPrimary Then primaryMatches.Add(f)
            End If
        Next

        If primaryMatches.Count > 0 Then
            validMatches = primaryMatches
        End If

        If validMatches.Count = 0 Then
            For Each f In rawMatches
                Dim n As String = If(f.Name, "")
                If Not n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) Then validMatches.Add(f)
            Next
        End If

        If validMatches.Count = 0 Then validMatches.AddRange(rawMatches)

        validMatches.Sort(New NameSorter(term))
        Return validMatches
    End Function

    Public Async Sub ExecuteSlotSearch(slot As SearchSlot, Optional preselectedFile As VaultFileInfo = Nothing)
        Await ExecuteSlotSearchAsync(slot, preselectedFile)
    End Sub

    Public Async Function ExecuteSlotSearchAsync(slot As SearchSlot, Optional preselectedFile As VaultFileInfo = Nothing) As Task
        If _isSearching Then Return
        If vaultBackend Is Nothing OrElse Not vaultBackend.IsConnected Then Return
        If slot Is Nothing Then Return

        ' Prune any other boxes whose text was cleared by the user
        For Each other In _searchSlots
            If Not Object.ReferenceEquals(other, slot) Then
                Dim otherText = If(other.TextBox IsNot Nothing, other.TextBox.Text, "")
                If String.IsNullOrWhiteSpace(otherText) Then
                    other.SearchTerm = ""
                    other.SelectedFile = Nothing
                    other.RootItem = Nothing
                End If
            End If
        Next

        Dim term As String = If(slot.TextBox IsNot Nothing, slot.TextBox.Text.Trim(), "")
        If String.IsNullOrEmpty(term) Then
            slot.SearchTerm = ""
            slot.SelectedFile = Nothing
            slot.RootItem = Nothing
            If Object.ReferenceEquals(_lastSearchedSlot, slot) Then
                _lastSearchedSlot = _searchSlots.LastOrDefault(Function(s) s.RootItem IsNot Nothing)
            End If
            _preferLastSearchForExport = True
            RebuildQueueFromSlots()
            lblStatus.Text = "Removed empty search from queue."
            Return
        End If

        Dim backend As IVaultBackend = vaultBackend
        Dim runId As Integer = BeginSearchUi(slot, term)
        Dim ct As CancellationToken = _searchCts.Token
        Dim errorToShow As String = Nothing

        Try
            Dim selectedFile As VaultFileInfo = preselectedFile

            If selectedFile Is Nothing Then
                ReportSearchProgress(runId, New SearchProgressInfo With {
                    .Detail = "Searching Vault for '" & term & "'...",
                    .LogLine = "Querying Vault files matching '" & term & "'..."
                })

                Dim validMatches As List(Of VaultFileInfo) = Await Task.Run(Function() SearchAndFilterMatches(backend, term, ct), ct)
                ct.ThrowIfCancellationRequested()

                If validMatches Is Nothing OrElse validMatches.Count = 0 Then
                    slot.SearchTerm = ""
                    slot.SelectedFile = Nothing
                    slot.RootItem = Nothing
                    If Object.ReferenceEquals(_lastSearchedSlot, slot) Then
                        _lastSearchedSlot = _searchSlots.LastOrDefault(Function(s) s.RootItem IsNot Nothing)
                    End If
                    RebuildQueueFromSlots()
                    AppendSearchLog("No matching files found for '" & term & "'.")
                    lblStatus.Text = "No file found for: " & term
                    Return
                End If

                If validMatches.Count = 1 Then
                    selectedFile = validMatches(0)
                    AppendSearchLog("Auto-selected single match: " & selectedFile.Name)
                Else
                    AppendSearchLog(String.Format("Found {0} matching files. Waiting for file selection...", validMatches.Count))
                    ReportSearchProgress(runId, New SearchProgressInfo With {
                        .Title = "Select a Drawing or Model",
                        .Detail = String.Format("Found {0} matches for '{1}'. Choose a file in the dialog...", validMatches.Count, term)
                    })

                    _searchStopwatch.Stop()
                    Dim selDlg As New SelectorForm(validMatches, term)
                    If selDlg.ShowDialog() = True AndAlso selDlg.SelectedFile IsNot Nothing Then
                        _searchStopwatch.Start()
                        selectedFile = selDlg.SelectedFile
                        AppendSearchLog("User selected: " & selectedFile.Name)
                    Else
                        AppendSearchLog("File selection cancelled.")
                        RebuildQueueFromSlots()
                        lblStatus.Text = "Ready."
                        Return
                    End If
                End If
            Else
                AppendSearchLog("Using preselected file: " & selectedFile.Name)
            End If

            If selectedFile Is Nothing Then Return
            ct.ThrowIfCancellationRequested()

            ReportSearchProgress(runId, New SearchProgressInfo With {
                .Title = "Building BOM: " & selectedFile.Name,
                .Detail = "Resolving root PDF for " & selectedFile.Name & "...",
                .LogLine = "Starting PDF-driven BOM extraction for " & selectedFile.Name & "..."
            })

            Dim slotFlatList As New List(Of PrintItem)()
            Dim slotProcessedIds As New HashSet(Of Long)()
            Dim progressCallback As Action(Of SearchProgressInfo) = Sub(p) ReportSearchProgress(runId, p)
            Dim rootItem As PrintItem = Await Task.Run(Function() BomTreeBuilder.BuildTree(backend, selectedFile, slotFlatList, slotProcessedIds, progressCallback, ct), ct)
            ct.ThrowIfCancellationRequested()

            If rootItem IsNot Nothing Then
                slot.SearchTerm = term
                slot.SelectedFile = selectedFile
                slot.RootItem = rootItem
                _lastSearchedSlot = slot
                _preferLastSearchForExport = True

                RebuildQueueFromSlots()
                Dim pdfCount = slotFlatList.Where(Function(p) p.IsPDF AndAlso Not p.IsMissing).Count()
                Dim missCount = slotFlatList.Where(Function(p) p.IsMissing).Count()
                Dim elapsedSec = _searchStopwatch.Elapsed.TotalSeconds
                Dim summary = String.Format("Loaded {0}: {1} PDFs resolved, {2} missing ({3:0.0}s).", rootItem.Name, pdfCount, missCount, elapsedSec)
                AppendSearchLog(summary)
                lblStatus.Text = summary
            Else
                slot.SearchTerm = ""
                slot.SelectedFile = Nothing
                slot.RootItem = Nothing
                If Object.ReferenceEquals(_lastSearchedSlot, slot) Then
                    _lastSearchedSlot = _searchSlots.LastOrDefault(Function(s) s.RootItem IsNot Nothing)
                End If
                RebuildQueueFromSlots()
                AppendSearchLog("No PDF drawing found for selection '" & selectedFile.Name & "'.")
                lblStatus.Text = "Could not build tree for selection."
            End If

        Catch oce As OperationCanceledException
            AppendSearchLog("Search cancelled by user.")
            lblStatus.Text = "Search cancelled."
        Catch ex As Exception
            AppendSearchLog("ERROR: " & ex.Message)
            lblStatus.Text = "Search error: " & ex.Message
            errorToShow = "Search Error: " & ex.Message & vbCrLf & ex.StackTrace
        Finally
            EndSearchUi()
        End Try

        If Not String.IsNullOrEmpty(errorToShow) Then
            MessageBox.Show(Me, errorToShow, "Search Error", MessageBoxButton.OK, MessageBoxImage.Error)
        End If
    End Function

    Private Sub RebuildQueueFromSlots()
        _flatList.Clear()
        _processedIds.Clear()

        Dim roots As New ObservableCollection(Of PrintItem)()
        For Each slot In _searchSlots
            If slot.RootItem IsNot Nothing Then
                ReindexTreeNode(slot.RootItem, _flatList, _processedIds)
                roots.Add(slot.RootItem)
            End If
        Next

        If roots.Count > 0 Then
            treeFiles.ItemsSource = roots
            If pnlEmptyQueueHint IsNot Nothing Then
                pnlEmptyQueueHint.Visibility = Visibility.Collapsed
            End If
        Else
            treeFiles.ItemsSource = Nothing
            If lstFiles IsNot Nothing Then
                lstFiles.ItemsSource = Nothing
            End If
            If pnlEmptyQueueHint IsNot Nothing Then
                pnlEmptyQueueHint.Visibility = Visibility.Visible
            End If
        End If

        ApplyFilter()

        If lstFiles IsNot Nothing AndAlso lstFiles.Visibility = Visibility.Visible Then
            If roots.Count > 0 Then
                RefreshListView()
            Else
                lstFiles.ItemsSource = Nothing
            End If
        End If
    End Sub

    Private Sub ReindexTreeNode(node As PrintItem, flatList As List(Of PrintItem), processedIds As HashSet(Of Long))
        If node Is Nothing Then Return

        If node.IsMissing OrElse node.FileId <= 0 Then
            node.IsDuplicate = False
            flatList.Add(node)
        Else
            Dim wasDup As Boolean = node.IsDuplicate
            Dim isNowDup As Boolean = processedIds.Contains(node.FileId)
            node.IsDuplicate = isNowDup

            If isNowDup Then
                node.IsChecked = False
            Else
                processedIds.Add(node.FileId)
                If wasDup Then
                    node.IsChecked = node.DefaultChecked
                End If
            End If
            flatList.Add(node)
        End If

        For Each child In node.Children
            ReindexTreeNode(child, flatList, processedIds)
        Next
    End Sub

    Private Sub ItemCheckBox_Click(sender As Object, e As RoutedEventArgs)
        Dim cb As CheckBox = TryCast(e.OriginalSource, CheckBox)
        If cb Is Nothing Then Return
        Dim item As PrintItem = TryCast(cb.DataContext, PrintItem)
        If item Is Nothing Then Return

        Dim isCheckedNow As Boolean = cb.IsChecked.GetValueOrDefault(False)
        ApplyCheckboxCascade(item, isCheckedNow)
    End Sub

    Public Sub ApplyCheckboxCascade(item As PrintItem, isCheckedNow As Boolean)
        If item Is Nothing Then Return
        item.IsChecked = isCheckedNow

        If item.Children.Count > 0 Then
            If Not isCheckedNow Then
                CascadeUncheck(item)
            Else
                CascadeRecheckEligible(item)
            End If
        End If

        UpdateActionButtonStates()
    End Sub

    Private Sub CascadeUncheck(parentItem As PrintItem)
        For Each child In parentItem.Children
            child.IsChecked = False
            If child.Children.Count > 0 Then
                CascadeUncheck(child)
            End If
        Next
    End Sub

    Private Sub CascadeRecheckEligible(parentItem As PrintItem)
        For Each child In parentItem.Children
            If child.IsPDF AndAlso Not child.IsMissing AndAlso Not child.IsDuplicate AndAlso child.IsReleased AndAlso Not BomTreeBuilder.IsGeneralArrangement(child.Name) Then
                child.IsChecked = True
            End If
            If child.Children.Count > 0 Then
                CascadeRecheckEligible(child)
            End If
        Next
    End Sub

    Private Sub TreeFiles_SelectedItemChanged(sender As Object, e As RoutedPropertyChangedEventArgs(Of Object)) Handles treeFiles.SelectedItemChanged
        If e.NewValue IsNot Nothing Then
            _preferLastSearchForExport = False
        End If
    End Sub

    Private Sub LstFiles_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles lstFiles.SelectionChanged
        If lstFiles IsNot Nothing AndAlso lstFiles.SelectedItem IsNot Nothing Then
            _preferLastSearchForExport = False
        End If
    End Sub

    Private Shared Function GetExportDirectory() As String
        Dim defaultPath As String = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        Try
            Dim settingsFile As String = IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VaultPrintTool", "exportdir.txt")
            If IO.File.Exists(settingsFile) Then
                Using reader As New IO.StreamReader(settingsFile)
                    Dim readPath = reader.ReadLine()
                    If readPath IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(readPath) AndAlso IO.Directory.Exists(readPath.TrimEnd("\"c, " "c)) Then
                        defaultPath = readPath.TrimEnd("\"c, " "c) & "\"
                    End If
                End Using
            End If
        Catch
        End Try
        Return defaultPath
    End Function

    ' Helper Sorter Class
    Public Class NameSorter
        Implements IComparer(Of VaultFileInfo)

        Private _term As String
        Public Sub New(term As String)
            _term = term
        End Sub

        Public Function Compare(x As VaultFileInfo, y As VaultFileInfo) As Integer Implements IComparer(Of VaultFileInfo).Compare
            Dim nA As String = If(x IsNot Nothing AndAlso Not String.IsNullOrEmpty(x.Name), x.Name, "Unknown")
            Dim nB As String = If(y IsNot Nothing AndAlso Not String.IsNullOrEmpty(y.Name), y.Name, "Unknown")

            Dim baseA = System.IO.Path.GetFileNameWithoutExtension(nA)
            Dim baseB = System.IO.Path.GetFileNameWithoutExtension(nB)
            Dim matchA = baseA.Equals(_term, StringComparison.OrdinalIgnoreCase)
            Dim matchB = baseB.Equals(_term, StringComparison.OrdinalIgnoreCase)

            If matchA AndAlso Not matchB Then Return -1
            If Not matchA AndAlso matchB Then Return 1

            Dim scoreA = GetExtScore(nA)
            Dim scoreB = GetExtScore(nB)
            If scoreA <> scoreB Then Return scoreB.CompareTo(scoreA)

            Return nA.Length.CompareTo(nB.Length)
        End Function

        Private Function GetExtScore(name As String) As Integer
            If name.EndsWith(".idw", StringComparison.OrdinalIgnoreCase) Then Return 100
            If name.EndsWith(".iam", StringComparison.OrdinalIgnoreCase) Then Return 90
            If name.EndsWith(".ipt", StringComparison.OrdinalIgnoreCase) Then Return 50
            If name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) Then Return -50
            If name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) Then Return -100
            Return 0
        End Function
    End Class

    Private Sub CollectCheckedPdfs(node As PrintItem, list As List(Of PrintItem))
        If node.IsChecked AndAlso node.IsPDF Then
            If Not list.Contains(node) Then list.Add(node)
        End If
        For Each child In node.Children
            CollectCheckedPdfs(child, list)
        Next
    End Sub

    Private Sub DownloadItemAndAttachments(backend As IVaultBackend, item As PrintItem, destDir As String, ByRef count As Integer, includeStp As Boolean, includeZip As Boolean, includeDxf As Boolean)
        lblStatus.Text = String.Format("Downloading: {0}", item.Name)
        Application.Current.Dispatcher.Invoke(Sub() lblStatus.UpdateLayout(), System.Windows.Threading.DispatcherPriority.Background)

        Dim localPath = backend.DownloadFile(item.FileId, item.Name, destDir, False)
        If Not String.IsNullOrEmpty(localPath) AndAlso System.IO.File.Exists(localPath) Then count += 1

        If includeStp OrElse includeZip OrElse includeDxf Then
            Dim rawName = item.Name
            If rawName.EndsWith(".idw.pdf", StringComparison.OrdinalIgnoreCase) Then rawName = rawName.Substring(0, rawName.Length - 8)
            If rawName.EndsWith(".ipt.pdf", StringComparison.OrdinalIgnoreCase) Then rawName = rawName.Substring(0, rawName.Length - 8)
            If rawName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) Then rawName = rawName.Substring(0, rawName.Length - 4)

            lblStatus.Text = "Searching for related files for " & rawName & "..."
            Application.Current.Dispatcher.Invoke(Sub() lblStatus.UpdateLayout(), System.Windows.Threading.DispatcherPriority.Background)

            Dim relatedFiles = backend.SearchFilesByName(rawName, False)
            If relatedFiles IsNot Nothing Then
                For Each relFile In relatedFiles
                    Dim relName As String = relFile.Name
                    If String.IsNullOrEmpty(relName) Then Continue For

                    Dim isMatch = False
                    If includeStp AndAlso (relName.Equals(rawName & ".idw.stp", StringComparison.OrdinalIgnoreCase) OrElse relName.Equals(rawName & ".ipt.stp", StringComparison.OrdinalIgnoreCase) OrElse relName.Equals(rawName & ".stp", StringComparison.OrdinalIgnoreCase)) Then isMatch = True
                    If includeZip AndAlso (relName.Equals(rawName & ".idw.zip", StringComparison.OrdinalIgnoreCase) OrElse relName.Equals(rawName & ".ipt.zip", StringComparison.OrdinalIgnoreCase) OrElse relName.Equals(rawName & ".zip", StringComparison.OrdinalIgnoreCase)) Then isMatch = True
                    If includeDxf AndAlso (relName.Equals(rawName & ".idw.dxf", StringComparison.OrdinalIgnoreCase) OrElse relName.Equals(rawName & ".ipt.dxf", StringComparison.OrdinalIgnoreCase) OrElse relName.Equals(rawName & ".dxf", StringComparison.OrdinalIgnoreCase)) Then isMatch = True

                    If isMatch AndAlso relFile.Id > 0 Then
                        lblStatus.Text = "Downloading " & relName & "..."
                        Application.Current.Dispatcher.Invoke(Sub() lblStatus.UpdateLayout(), System.Windows.Threading.DispatcherPriority.Background)
                        Dim matchLocalPath = backend.DownloadFile(relFile.Id, relName, destDir, False)
                        If Not String.IsNullOrEmpty(matchLocalPath) AndAlso System.IO.File.Exists(matchLocalPath) Then count += 1
                    End If
                Next
            End If
        End If
    End Sub

    Private Sub CleanExportDirectory(targetDir As String)
        Try
            If Not Directory.Exists(targetDir) Then Return
            Dim dirs = Directory.GetDirectories(targetDir, "*", SearchOption.AllDirectories)
            Dim vDirs = dirs.Where(Function(d) Path.GetFileName(d).Equals("_V", StringComparison.OrdinalIgnoreCase)).OrderByDescending(Function(d) d.Length).ToList()
            For Each vDir In vDirs
                Try
                    Dim di As New DirectoryInfo(vDir)
                    If di.Exists Then
                        For Each fi In di.GetFiles("*", SearchOption.AllDirectories)
                            Try : fi.Attributes = FileAttributes.Normal : Catch : End Try
                        Next
                        For Each subDi In di.GetDirectories("*", SearchOption.AllDirectories)
                            Try : subDi.Attributes = FileAttributes.Normal : Catch : End Try
                        Next
                        di.Attributes = FileAttributes.Normal
                        di.Delete(True)
                    End If
                Catch
                End Try
            Next
        Catch
        End Try
    End Sub

    Private Shared Function StripDrawingExtensions(fileName As String) As String
        Dim baseName = Path.GetFileNameWithoutExtension(If(fileName, ""))
        If baseName.EndsWith(".idw", StringComparison.OrdinalIgnoreCase) Then baseName = Path.GetFileNameWithoutExtension(baseName)
        If baseName.EndsWith(".iam", StringComparison.OrdinalIgnoreCase) Then baseName = Path.GetFileNameWithoutExtension(baseName)
        If baseName.EndsWith(".ipt", StringComparison.OrdinalIgnoreCase) Then baseName = Path.GetFileNameWithoutExtension(baseName)
        Return baseName
    End Function

    Private Shared Function ContainsNode(root As PrintItem, target As PrintItem) As Boolean
        If root Is Nothing OrElse target Is Nothing Then Return False
        If Object.ReferenceEquals(root, target) Then Return True
        For Each child In root.Children
            If ContainsNode(child, target) Then Return True
        Next
        Return False
    End Function

    Public Function GetCurrentRootForExport(requireCheckedPdf As Boolean) As PrintItem
        Dim roots = TryCast(treeFiles.ItemsSource, ObservableCollection(Of PrintItem))
        If roots Is Nothing OrElse roots.Count = 0 Then
            Return _flatList.FirstOrDefault()
        End If

        If Not _preferLastSearchForExport Then
            Dim selectedItem As PrintItem = Nothing
            If lstFiles IsNot Nothing AndAlso lstFiles.Visibility = Visibility.Visible Then
                selectedItem = TryCast(lstFiles.SelectedItem, PrintItem)
            End If
            If selectedItem Is Nothing Then
                selectedItem = TryCast(treeFiles.SelectedItem, PrintItem)
            End If
            If selectedItem IsNot Nothing Then
                For Each r In roots
                    If ContainsNode(r, selectedItem) Then
                        If Not requireCheckedPdf OrElse CheckEnableFlags(r) Then Return r
                    End If
                Next
            End If
        End If

        If _lastSearchedSlot IsNot Nothing AndAlso _lastSearchedSlot.RootItem IsNot Nothing AndAlso roots.Contains(_lastSearchedSlot.RootItem) Then
            If Not requireCheckedPdf OrElse CheckEnableFlags(_lastSearchedSlot.RootItem) Then
                Return _lastSearchedSlot.RootItem
            End If
        End If

        For i As Integer = roots.Count - 1 To 0 Step -1
            Dim r = roots(i)
            If Not requireCheckedPdf OrElse CheckEnableFlags(r) Then Return r
        Next

        Return roots(roots.Count - 1)
    End Function

    Public Function GetDefaultZipFileName() As String
        Dim targetRoot As PrintItem = GetCurrentRootForExport(True)
        If targetRoot Is Nothing Then targetRoot = GetCurrentRootForExport(False)
        If targetRoot Is Nothing Then Return "Export.zip"

        Dim baseName = StripDrawingExtensions(targetRoot.Name)
        Dim rev As String = If(targetRoot.Revision, "")
        If String.IsNullOrWhiteSpace(rev) AndAlso vaultBackend IsNot Nothing AndAlso vaultBackend.IsConnected AndAlso targetRoot.FileId > 0 Then
            Dim det = vaultBackend.GetFileDetails(targetRoot.FileId)
            If det IsNot Nothing Then
                rev = det.Revision
                targetRoot.Revision = rev
            End If
        End If

        If Not String.IsNullOrEmpty(rev) Then
            If baseName.IndexOf("Rev", StringComparison.OrdinalIgnoreCase) >= 0 Then
                Return baseName & ".zip"
            Else
                Return String.Format("{0} Rev {1}.zip", baseName, rev)
            End If
        End If
        Return baseName & ".zip"
    End Function

    Private Sub BtnExportZip_Click(sender As Object, e As RoutedEventArgs) Handles btnExportZip.Click
        If vaultBackend Is Nothing OrElse Not vaultBackend.IsConnected Then Return

        Dim toExport = _flatList.Where(Function(p) p.IsChecked AndAlso p.IsPDF).ToList()

        If toExport.Count = 0 Then
            MessageBox.Show("No PDFs selected to export.")
            Return
        End If

        Dim zipFileName As String = GetDefaultZipFileName()

        Dim sfd As New Microsoft.Win32.SaveFileDialog()
        sfd.Filter = "ZIP Archive (*.zip)|*.zip"
        sfd.FileName = zipFileName
        sfd.InitialDirectory = GetExportDirectory()
        sfd.Title = "Export PDFs to ZIP"

        If sfd.ShowDialog() = True Then
            lblStatus.Text = "Preparing ZIP export..."

            Dim tempDir = Path.Combine(Path.GetTempPath(), "VaultPrintZip_" & DateTime.Now.Ticks)
            System.IO.Directory.CreateDirectory(tempDir)

            Dim finalZipPath = sfd.FileName

            Application.Current.Dispatcher.Invoke(Sub()
                Try
                    Dim includeStp = chkIncludeStp.IsChecked.GetValueOrDefault()
                    Dim includeZip = chkIncludeZip.IsChecked.GetValueOrDefault()
                    Dim includeDxf = chkIncludeDxf.IsChecked.GetValueOrDefault()
                    Dim groupSubfolders = chkGroupSubfolders.IsChecked.GetValueOrDefault()
                    Dim count As Integer = 0

                    If groupSubfolders AndAlso treeFiles.ItemsSource IsNot Nothing Then
                        Dim roots As ObservableCollection(Of PrintItem) = DirectCast(treeFiles.ItemsSource, ObservableCollection(Of PrintItem))
                        For Each r In roots
                            Dim rootExportList As New List(Of PrintItem)
                            CollectCheckedPdfs(r, rootExportList)

                            If rootExportList.Count = 0 Then Continue For

                            Dim rBase = Path.GetFileNameWithoutExtension(r.Name)
                            If rBase.EndsWith(".idw", StringComparison.OrdinalIgnoreCase) Then rBase = Path.GetFileNameWithoutExtension(rBase)
                            If rBase.EndsWith(".iam", StringComparison.OrdinalIgnoreCase) Then rBase = Path.GetFileNameWithoutExtension(rBase)
                            If rBase.EndsWith(".ipt", StringComparison.OrdinalIgnoreCase) Then rBase = Path.GetFileNameWithoutExtension(rBase)
                            Dim destDir = Path.Combine(tempDir, rBase)
                            If Not Directory.Exists(destDir) Then Directory.CreateDirectory(destDir)

                            Dim processedIds As New HashSet(Of Long)
                            For Each item In rootExportList
                                If processedIds.Contains(item.FileId) Then Continue For
                                processedIds.Add(item.FileId)

                                DownloadItemAndAttachments(vaultBackend, item, destDir, count, includeStp, includeZip, includeDxf)
                            Next
                        Next
                    Else
                        For Each item In toExport
                            DownloadItemAndAttachments(vaultBackend, item, tempDir, count, includeStp, includeZip, includeDxf)
                        Next
                    End If

                    lblStatus.Text = "Zipping files..."
                    Application.Current.Dispatcher.Invoke(Sub() lblStatus.UpdateLayout(), System.Windows.Threading.DispatcherPriority.Background)

                    CleanExportDirectory(tempDir)

                    If System.IO.File.Exists(finalZipPath) Then System.IO.File.Delete(finalZipPath)
                    ZipFile.CreateFromDirectory(tempDir, finalZipPath)

                    lblStatus.Text = String.Format("Exported {0} files to {1}.", count, Path.GetFileName(finalZipPath))
                    MessageBox.Show(Me, "Export Complete.")
                Catch ex As Exception
                    MessageBox.Show(Me, "Export Error: " & ex.Message)
                Finally
                    Try : Directory.Delete(tempDir, True) : Catch : End Try
                End Try
            End Sub, System.Windows.Threading.DispatcherPriority.Background)
        End If
    End Sub

    Private Sub BtnPrint_Click(sender As Object, e As RoutedEventArgs) Handles btnPrint.Click
        If vaultBackend Is Nothing OrElse Not vaultBackend.IsConnected Then Return

        Dim toPrint = _flatList.Where(Function(p) p.IsChecked AndAlso p.IsPDF).ToList()

        If toPrint.Count = 0 Then
            MessageBox.Show("No PDFs selected to print.")
            Return
        End If

        Dim ps = New PrintDialog()
        If ps.ShowDialog() = True Then
            Dim queue = ps.PrintQueue

            Dim confirm = MessageBox.Show(Me, String.Format("Send {0} files to '{1}'?", toPrint.Count, queue.FullName), "Confirm Print", MessageBoxButton.YesNo)
            If confirm = MessageBoxResult.No Then Return

            lblStatus.Text = "Downloading and Printing..."

            Dim tempDir = Path.Combine(Path.GetTempPath(), "VaultPrint_" & DateTime.Now.Ticks)
            System.IO.Directory.CreateDirectory(tempDir)

            Try
                Dim count As Integer = 0
                For Each item In toPrint
                    lblStatus.Text = String.Format("Printing {0}/{1}: {2}", count + 1, toPrint.Count, item.Name)
                    Application.Current.Dispatcher.Invoke(Sub() lblStatus.UpdateLayout(), System.Windows.Threading.DispatcherPriority.Background)

                    Dim localPath = vaultBackend.DownloadFile(item.FileId, item.Name, tempDir, False)
                    If Not String.IsNullOrEmpty(localPath) AndAlso System.IO.File.Exists(localPath) Then
                        PrintManager.PrintPDF(localPath, queue.FullName)
                        count += 1
                    End If
                Next
                lblStatus.Text = String.Format("Sent {0} jobs to printer.", count)
                MessageBox.Show(Me, "Printing Complete.")
            Catch ex As Exception
                MessageBox.Show(Me, "Print Error: " & ex.Message)
            Finally
                Try : Directory.Delete(tempDir, True) : Catch : End Try
            End Try
        End If
    End Sub

    Private Sub BtnExportText_Click(sender As Object, e As RoutedEventArgs) Handles btnExportText.Click
        Dim items = TryCast(treeFiles.ItemsSource, ObservableCollection(Of PrintItem))
        If items Is Nothing OrElse items.Count = 0 Then
            MessageBox.Show("The list is empty.", "Nothing to Export")
            Return
        End If

        Dim saveDlg As New Microsoft.Win32.SaveFileDialog()
        saveDlg.Filter = "Text Document|*.txt|CSV File|*.csv"
        saveDlg.Title = "Export List to Text or CSV"
        saveDlg.InitialDirectory = GetExportDirectory()

        Dim dName As String = "PrintList_" & DateTime.Now.ToString("yyyyMMdd_HHmmss")
        Dim targetRoot = GetCurrentRootForExport(False)
        If targetRoot IsNot Nothing AndAlso Not String.IsNullOrEmpty(targetRoot.Name) Then
            dName = StripDrawingExtensions(targetRoot.Name) & "_List"
        End If
        saveDlg.FileName = dName & ".txt"

        If saveDlg.ShowDialog() = True Then
            Try
                Dim isCsv = saveDlg.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
                Dim sb As New Text.StringBuilder()

                If isCsv Then sb.AppendLine("Level,Part Number,Status")

                For Each rootItem As PrintItem In items
                    For Each child In rootItem.Children
                        If isCsv Then
                            AppendCsvNode(child, 0, sb)
                        Else
                            AppendTextNode(child, 0, sb)
                        End If
                    Next
                Next
                System.IO.File.WriteAllText(saveDlg.FileName, sb.ToString())
                MessageBox.Show("List exported successfully.", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information)
            Catch ex As Exception
                MessageBox.Show("Failed to save text file: " & ex.Message, "Export Error", MessageBoxButton.OK, MessageBoxImage.Error)
            End Try
        End If
    End Sub

    Private Sub BtnExportMissing_Click(sender As Object, e As RoutedEventArgs) Handles btnExportMissing.Click
        Dim missingItems = _flatList.Where(Function(p) p.IsMissing).Select(Function(p) p.Name).Distinct().ToList()
        If missingItems.Count = 0 Then
            MessageBox.Show("There are no missing files in the current list.", "None Missing")
            Return
        End If

        Dim saveDlg As New Microsoft.Win32.SaveFileDialog()
        saveDlg.Filter = "Text Document|*.txt|CSV File|*.csv"
        saveDlg.Title = "Export Missing Files"
        saveDlg.InitialDirectory = GetExportDirectory()

        Dim dName = "MissingFiles_" & DateTime.Now.ToString("yyyyMMdd_HHmmss")
        Dim targetRoot = GetCurrentRootForExport(False)
        If targetRoot IsNot Nothing AndAlso Not String.IsNullOrEmpty(targetRoot.Name) Then
            dName = StripDrawingExtensions(targetRoot.Name) & "_Missing"
        End If
        saveDlg.FileName = dName & ".txt"

        If saveDlg.ShowDialog() = True Then
            Try
                Dim isCsv = saveDlg.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
                Dim sb As New Text.StringBuilder()

                If isCsv Then sb.AppendLine("Part Number")

                For Each mn In missingItems
                    Dim rawName = mn
                    If rawName.EndsWith(".idw.pdf", StringComparison.OrdinalIgnoreCase) Then rawName = rawName.Substring(0, rawName.Length - 8)
                    If rawName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) Then rawName = rawName.Substring(0, rawName.Length - 4)
                    sb.AppendLine(rawName)
                Next

                System.IO.File.WriteAllText(saveDlg.FileName, sb.ToString())
                MessageBox.Show("Missing files exported successfully.", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information)
            Catch ex As Exception
                MessageBox.Show("Failed to save missing list: " & ex.Message, "Export Error", MessageBoxButton.OK, MessageBoxImage.Error)
            End Try
        End If
    End Sub

    Private Sub AppendTextNode(node As PrintItem, depth As Integer, sb As Text.StringBuilder)
        If Not node.IsPDF Then Return

        Dim indent As String = New String(" "c, depth * 2)
        Dim statusBox As String = ""

        If node.IsMissing Then
            statusBox = "[MISSING]"
        ElseIf node.IsChecked Then
            statusBox = "[X]"
        Else
            statusBox = "[ ]"
        End If

        sb.AppendLine(indent & statusBox & " " & node.Name)

        For Each child In node.Children
            AppendTextNode(child, depth + 1, sb)
        Next
    End Sub

    Private Sub AppendCsvNode(node As PrintItem, depth As Integer, sb As Text.StringBuilder)
        If Not node.IsPDF Then Return

        Dim statusText As String = "Deselected"
        If node.IsMissing Then
            statusText = "Missing"
        ElseIf node.IsChecked Then
            statusText = "Selected"
        End If

        Dim rawName = node.Name
        If rawName.EndsWith(".idw.pdf", StringComparison.OrdinalIgnoreCase) Then rawName = rawName.Substring(0, rawName.Length - 8)
        If rawName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) Then rawName = rawName.Substring(0, rawName.Length - 4)

        Dim safeName = rawName
        If safeName.Contains(",") OrElse safeName.Contains("""") Then
            safeName = """" & safeName.Replace("""", """""") & """"
        End If

        sb.AppendLine(String.Format("{0},{1},{2}", depth, safeName, statusText))

        For Each child In node.Children
            AppendCsvNode(child, depth + 1, sb)
        Next
    End Sub

    Private Sub RefreshListView()
        Dim sortedList = _flatList.Where(Function(p) p.IsPDF).OrderByDescending(Function(p) p.IsChecked).ThenBy(Function(p) p.Name).ToList()
        lstFiles.ItemsSource = sortedList
    End Sub

    Private Sub ApplyFilter()
        If treeFiles.ItemsSource IsNot Nothing Then
            If _hideUnticked Then
                btnToggleFilter.Content = "📂 Show All Files"
                btnToggleFilter.ToolTip = "Show all drawings in the queue (including unchecked items)"
                For Each rootItem As PrintItem In treeFiles.ItemsSource
                    ApplyFilterRecursive(rootItem)
                Next
            Else
                btnToggleFilter.Content = "👁 Show Selected Only"
                btnToggleFilter.ToolTip = "Filter queue to display only the drawings currently checked for printing/export"
                For Each item In _flatList
                    item.UIVisibility = Visibility.Visible
                Next
            End If
        Else
            If _hideUnticked Then
                btnToggleFilter.Content = "📂 Show All Files"
            Else
                btnToggleFilter.Content = "👁 Show Selected Only"
            End If
        End If

        UpdateActionButtonStates()
    End Sub

    Private Sub UpdateActionButtonStates()
        Dim hasFiles As Boolean = False
        Dim hasMissing As Boolean = False
        Dim items = TryCast(treeFiles.ItemsSource, ObservableCollection(Of PrintItem))
        If items IsNot Nothing Then
            For Each child In items
                If CheckEnableFlags(child) Then hasFiles = True
            Next
            btnExportText.IsEnabled = (items.Count > 0)
            hasMissing = _flatList.Any(Function(p) p.IsMissing)
        Else
            btnExportText.IsEnabled = False
        End If

        btnExportMissing.IsEnabled = hasMissing
        btnExportZip.IsEnabled = hasFiles
        btnPrint.IsEnabled = hasFiles
    End Sub

    Private Function CheckEnableFlags(n As PrintItem) As Boolean
        If n.IsChecked AndAlso n.IsPDF Then Return True
        For Each c In n.Children
            If CheckEnableFlags(c) Then Return True
        Next
        Return False
    End Function

    Private Function ApplyFilterRecursive(item As PrintItem) As Boolean
        Dim hasVisibleChild As Boolean = False
        For Each child In item.Children
            If ApplyFilterRecursive(child) Then
                hasVisibleChild = True
            End If
        Next

        Dim isVisible As Boolean = item.IsChecked OrElse hasVisibleChild
        item.UIVisibility = If(isVisible, Visibility.Visible, Visibility.Collapsed)
        Return isVisible
    End Function

    Private Sub BtnToggleFilter_Click(sender As Object, e As RoutedEventArgs) Handles btnToggleFilter.Click
        _hideUnticked = Not _hideUnticked
        ApplyFilter()
    End Sub

    Private Sub BtnToggleView_Click(sender As Object, e As RoutedEventArgs) Handles btnToggleView.Click
        If treeFiles.Visibility = Visibility.Visible Then
            treeFiles.Visibility = Visibility.Collapsed
            lstFiles.Visibility = Visibility.Visible
            btnToggleView.Content = "🌲 Switch to Tree View"
            btnToggleView.ToolTip = "Switch back to hierarchical assembly tree view (shows parent & sub-assemblies)"
            RefreshListView()
        Else
            lstFiles.Visibility = Visibility.Collapsed
            treeFiles.Visibility = Visibility.Visible
            btnToggleView.Content = "📋 Switch to List View"
            btnToggleView.ToolTip = "Switch to flat list view (shows all drawings in a single sorted list)"
        End If
    End Sub

End Class


' --- Helper Dialog ---
Public Class SelectorForm
    Inherits Window

    Public Property SelectedFile As VaultFileInfo
    Private _lst As ListBox
    Private _map As New Dictionary(Of Integer, VaultFileInfo)

    Public Sub New(files As List(Of VaultFileInfo), Optional searchTerm As String = "")
        Me.Title = "Select File (" & files.Count & " matches)"
        Me.Width = 680
        Me.Height = 520
        Me.WindowStartupLocation = WindowStartupLocation.CenterScreen
        Me.Topmost = True
        Me.Background = New SolidColorBrush(Color.FromRgb(30, 30, 30))

        Dim grid As New Grid()
        grid.RowDefinitions.Add(New RowDefinition() With {.Height = GridLength.Auto})
        grid.RowDefinitions.Add(New RowDefinition() With {.Height = New GridLength(1, GridUnitType.Star)})
        grid.RowDefinitions.Add(New RowDefinition() With {.Height = GridLength.Auto})

        ' 1. Header with clear instructions
        Dim pnlHeader As New StackPanel()
        pnlHeader.Margin = New Thickness(14, 14, 14, 8)

        Dim lblTitle As New TextBlock()
        If Not String.IsNullOrEmpty(searchTerm) Then
            lblTitle.Text = String.Format("Matching Files in Vault for '{0}' ({1} found)", searchTerm, files.Count)
        Else
            lblTitle.Text = String.Format("Select File ({0} matches found)", files.Count)
        End If
        lblTitle.FontSize = 15
        lblTitle.FontWeight = FontWeights.Bold
        lblTitle.Foreground = Brushes.White
        lblTitle.Margin = New Thickness(0, 0, 0, 4)
        pnlHeader.Children.Add(lblTitle)

        Dim lblDesc As New TextBlock()
        lblDesc.Text = "Please select the file you want to process. For assemblies, selecting the top-level drawing (*.idw) (highlighted as Recommended below) is suggested to automatically parse the drawing BOM and load the full drawing package. However, you can select any other drawing or model if you only need that specific component."
        lblDesc.Foreground = New SolidColorBrush(Color.FromRgb(&HCC, &HCC, &HCC))
        lblDesc.FontSize = 11.5
        lblDesc.TextWrapping = TextWrapping.Wrap
        lblDesc.LineHeight = 16
        pnlHeader.Children.Add(lblDesc)

        Grid.SetRow(pnlHeader, 0)
        grid.Children.Add(pnlHeader)

        ' 2. ListBox
        _lst = New ListBox()
        _lst.Margin = New Thickness(14, 0, 14, 10)
        _lst.Background = New SolidColorBrush(Color.FromRgb(&H22, &H22, &H24))
        _lst.BorderBrush = New SolidColorBrush(Color.FromRgb(&H3F, &H3F, &H46))
        _lst.BorderThickness = New Thickness(1)
        _lst.Foreground = Brushes.White
        _lst.HorizontalContentAlignment = HorizontalAlignment.Stretch
        _lst.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled)
        AddHandler _lst.MouseDoubleClick, AddressOf lst_DblClick
        Grid.SetRow(_lst, 1)
        grid.Children.Add(_lst)

        ' 3. Bottom controls
        Dim pnlBottom As New Grid()
        pnlBottom.Margin = New Thickness(14, 0, 14, 14)
        pnlBottom.ColumnDefinitions.Add(New ColumnDefinition() With {.Width = New GridLength(1, GridUnitType.Star)})
        pnlBottom.ColumnDefinitions.Add(New ColumnDefinition() With {.Width = GridLength.Auto})

        Dim lblTip As New TextBlock()
        lblTip.Text = "💡 Pick the drawing or model you want to process, then click Select."
        lblTip.Foreground = New SolidColorBrush(Color.FromRgb(&H88, &H88, &H88))
        lblTip.FontSize = 11
        lblTip.VerticalAlignment = VerticalAlignment.Center
        Grid.SetColumn(lblTip, 0)
        pnlBottom.Children.Add(lblTip)

        Dim btnPanel As New StackPanel()
        btnPanel.Orientation = Orientation.Horizontal
        Grid.SetColumn(btnPanel, 1)

        Dim _btnOk As New Button()
        _btnOk.Content = "Select"
        _btnOk.Width = 110
        _btnOk.Height = 35
        _btnOk.Background = New SolidColorBrush(Color.FromRgb(0, 122, 204))
        _btnOk.Foreground = Brushes.White
        _btnOk.FontWeight = FontWeights.SemiBold
        _btnOk.IsDefault = True
        _btnOk.BorderThickness = New Thickness(0)
        AddHandler _btnOk.Click, AddressOf btnOk_Click
        btnPanel.Children.Add(_btnOk)

        Dim _btnCancel As New Button()
        _btnCancel.Content = "Cancel"
        _btnCancel.Width = 80
        _btnCancel.Height = 35
        _btnCancel.Margin = New Thickness(8, 0, 0, 0)
        _btnCancel.Background = New SolidColorBrush(Color.FromRgb(&H33, &H33, &H33))
        _btnCancel.Foreground = New SolidColorBrush(Color.FromRgb(&HCC, &HCC, &HCC))
        _btnCancel.IsCancel = True
        _btnCancel.BorderThickness = New Thickness(0)
        AddHandler _btnCancel.Click, Sub(s, e)
                                         Me.DialogResult = False
                                         Me.Close()
                                     End Sub
        btnPanel.Children.Add(_btnCancel)

        pnlBottom.Children.Add(btnPanel)
        Grid.SetRow(pnlBottom, 2)
        grid.Children.Add(pnlBottom)

        Me.Content = grid

        ' 4. Determine Best Match (Recommended IDW)
        Dim fileNames As New List(Of String)
        Dim bestIndex As Integer = 0
        Dim maxScore As Integer = Integer.MinValue

        For i As Integer = 0 To files.Count - 1
            Dim f = files(i)
            Dim name As String = If(f IsNot Nothing AndAlso Not String.IsNullOrEmpty(f.Name), f.Name, "Unknown Item")
            fileNames.Add(name)

            Dim score = ScoreCandidate(name, searchTerm)
            If score > maxScore Then
                maxScore = score
                bestIndex = i
            End If
        Next

        ' 5. Populate ListBox with badges & styling
        Try
            For i As Integer = 0 To files.Count - 1
                Dim f = files(i)
                Dim name = fileNames(i)
                Dim isRecommended = (i = bestIndex)

                Dim isIdw = name.EndsWith(".idw", StringComparison.OrdinalIgnoreCase)
                Dim isIam = name.EndsWith(".iam", StringComparison.OrdinalIgnoreCase)
                Dim isIpt = name.EndsWith(".ipt", StringComparison.OrdinalIgnoreCase)

                Dim itemBorder As New Border()
                itemBorder.CornerRadius = New CornerRadius(4)

                If isRecommended Then
                    itemBorder.Background = New SolidColorBrush(Color.FromRgb(&H17, &H30, &H48))
                    itemBorder.BorderBrush = New SolidColorBrush(Color.FromRgb(0, 122, 204))
                    itemBorder.BorderThickness = New Thickness(1.5)
                    itemBorder.Padding = New Thickness(8, 6, 8, 6)
                    itemBorder.Margin = New Thickness(2, 2, 2, 4)
                Else
                    itemBorder.Background = New SolidColorBrush(Color.FromRgb(&H2A, &H2A, &H2D))
                    itemBorder.BorderBrush = New SolidColorBrush(Color.FromRgb(&H38, &H38, &H3C))
                    itemBorder.BorderThickness = New Thickness(1)
                    itemBorder.Padding = New Thickness(8, 5, 8, 5)
                    itemBorder.Margin = New Thickness(2, 1, 2, 2)
                End If

                Dim itemSp As New StackPanel()
                Dim topRow As New StackPanel() With {.Orientation = Orientation.Horizontal}

                Dim badge As New Border()
                badge.CornerRadius = New CornerRadius(3)
                badge.Padding = New Thickness(5, 2, 5, 2)
                badge.Margin = New Thickness(0, 0, 8, 0)
                badge.VerticalAlignment = VerticalAlignment.Center

                Dim badgeTxt As New TextBlock()
                badgeTxt.FontSize = 10
                badgeTxt.FontWeight = FontWeights.Bold

                If isRecommended Then
                    badge.Background = New SolidColorBrush(Color.FromRgb(0, 122, 204))
                    badgeTxt.Foreground = Brushes.White
                    badgeTxt.Text = "★ RECOMMENDED DRAWING"
                ElseIf isIdw Then
                    badge.Background = New SolidColorBrush(Color.FromRgb(&H3A, &H42, &H4D))
                    badgeTxt.Foreground = New SolidColorBrush(Color.FromRgb(&H9A, &HC0, &HE8))
                    badgeTxt.Text = "DRAWING (.IDW)"
                ElseIf isIam Then
                    badge.Background = New SolidColorBrush(Color.FromRgb(&H4D, &H38, &H20))
                    badgeTxt.Foreground = New SolidColorBrush(Color.FromRgb(&HF0, &HB0, &H60))
                    badgeTxt.Text = "ASSEMBLY (.IAM)"
                ElseIf isIpt Then
                    badge.Background = New SolidColorBrush(Color.FromRgb(&H2D, &H35, &H3D))
                    badgeTxt.Foreground = New SolidColorBrush(Color.FromRgb(&H88, &HB0, &HD0))
                    badgeTxt.Text = "PART (.IPT)"
                Else
                    badge.Background = New SolidColorBrush(Color.FromRgb(&H33, &H33, &H33))
                    badgeTxt.Foreground = Brushes.Gray
                    badgeTxt.Text = "FILE"
                End If

                badge.Child = badgeTxt
                topRow.Children.Add(badge)

                Dim txtName As New TextBlock()
                txtName.Text = name
                txtName.FontSize = If(isRecommended, 13.0, 12.0)
                txtName.FontWeight = If(isRecommended, FontWeights.Bold, FontWeights.Normal)
                txtName.Foreground = If(isRecommended, Brushes.White, If(isIdw, Brushes.White, If(isIam, New SolidColorBrush(Color.FromRgb(&HD0, &HD0, &HD0)), New SolidColorBrush(Color.FromRgb(&HB0, &HB0, &HB0)))))
                txtName.VerticalAlignment = VerticalAlignment.Center
                topRow.Children.Add(txtName)

                itemSp.Children.Add(topRow)

                If isRecommended Then
                    Dim txtSub As New TextBlock()
                    txtSub.Text = "Top-level assembly drawing — parses full BOM and all sub-drawings"
                    txtSub.FontSize = 10.5
                    txtSub.Foreground = New SolidColorBrush(Color.FromRgb(&H9C, &HDC, &HFE))
                    txtSub.Margin = New Thickness(0, 3, 0, 0)
                    itemSp.Children.Add(txtSub)
                ElseIf isIdw AndAlso (name.Contains("_") OrElse name.StartsWith("OLD ", StringComparison.OrdinalIgnoreCase)) Then
                    Dim txtSub As New TextBlock()
                    If name.StartsWith("OLD ", StringComparison.OrdinalIgnoreCase) Then
                        txtSub.Text = "Archived / superseded drawing"
                        txtSub.Foreground = Brushes.Tomato
                    Else
                        txtSub.Text = "Individual component drawing"
                        txtSub.Foreground = New SolidColorBrush(Color.FromRgb(&H88, &H88, &H88))
                    End If
                    txtSub.FontSize = 10.0
                    txtSub.Margin = New Thickness(0, 2, 0, 0)
                    itemSp.Children.Add(txtSub)
                End If

                itemBorder.Child = itemSp
                _lst.Items.Add(itemBorder)
                _map(i) = f
            Next
        Catch ex As Exception
            _lst.Items.Add("Error populating: " & ex.Message)
        End Try

        If _lst.Items.Count > 0 Then _lst.SelectedIndex = bestIndex
    End Sub

    Private Shared Function ScoreCandidate(fName As String, term As String) As Integer
        Dim score As Integer = 0
        Dim isIdw = fName.EndsWith(".idw", StringComparison.OrdinalIgnoreCase)
        Dim isIam = fName.EndsWith(".iam", StringComparison.OrdinalIgnoreCase)
        Dim isIpt = fName.EndsWith(".ipt", StringComparison.OrdinalIgnoreCase)

        If isIdw Then
            score += 1000
        ElseIf isIam Then
            score += 400
        ElseIf isIpt Then
            score += 100
        End If

        Dim base = IO.Path.GetFileNameWithoutExtension(fName)

        If base.StartsWith("OLD ", StringComparison.OrdinalIgnoreCase) OrElse
           base.IndexOf("OBSOLETE", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
           base.IndexOf("COPY", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
           base.IndexOf("BAK", StringComparison.OrdinalIgnoreCase) >= 0 Then
            score -= 500
        End If

        Dim cleanTerm = term.Trim()
        If Not String.IsNullOrEmpty(cleanTerm) Then
            Dim spIdx = base.IndexOf(" "c)
            Dim partNo = If(spIdx > 0, base.Substring(0, spIdx), base)

            If String.Equals(partNo, cleanTerm, StringComparison.OrdinalIgnoreCase) Then
                score += 800
            ElseIf partNo.StartsWith(cleanTerm & "_", StringComparison.OrdinalIgnoreCase) Then
                score += 100
            ElseIf base.IndexOf(cleanTerm, StringComparison.OrdinalIgnoreCase) >= 0 Then
                score += 50
            End If
        End If

        score -= Math.Min(base.Length, 100)
        Return score
    End Function

    Private Sub btnOk_Click(s As Object, e As RoutedEventArgs)
        If _lst.SelectedIndex >= 0 Then
            SelectedFile = _map(_lst.SelectedIndex)
            Me.DialogResult = True
            Me.Close()
        End If
    End Sub

    Private Sub lst_DblClick(s As Object, e As System.Windows.Input.MouseButtonEventArgs)
        If _lst.SelectedIndex >= 0 Then
            SelectedFile = _map(_lst.SelectedIndex)
            Me.DialogResult = True
            Me.Close()
        End If
    End Sub
End Class

' --- Data Model ---
Public Class PrintItem
    Implements System.ComponentModel.INotifyPropertyChanged

    Public Event PropertyChanged As System.ComponentModel.PropertyChangedEventHandler Implements System.ComponentModel.INotifyPropertyChanged.PropertyChanged

    Protected Sub OnPropertyChanged(name As String)
        RaiseEvent PropertyChanged(Me, New System.ComponentModel.PropertyChangedEventArgs(name))
    End Sub

    Public Property Name As String
    Public Property FileId As Long
    Public Property IsPDF As Boolean
    Public Property IsReleased As Boolean
    Public Property Revision As String = ""
    Public Property IsMissing As Boolean
    Public Property DefaultChecked As Boolean

    Private _isDuplicate As Boolean
    Public Property IsDuplicate As Boolean
        Get
            Return _isDuplicate
        End Get
        Set(value As Boolean)
            If _isDuplicate <> value Then
                _isDuplicate = value
                OnPropertyChanged("IsDuplicate")
                OnPropertyChanged("StatusText")
                OnPropertyChanged("StatusColor")
                OnPropertyChanged("IsEnabled")
            End If
        End Set
    End Property

    Private _isExpanded As Boolean = True
    Public Property IsExpanded As Boolean
        Get
            Return _isExpanded
        End Get
        Set(value As Boolean)
            _isExpanded = value
            OnPropertyChanged("IsExpanded")
        End Set
    End Property

    Public Property Children As New ObservableCollection(Of PrintItem)

    Private _isChecked As Boolean
    Public Property IsChecked As Boolean
        Get
            Return _isChecked
        End Get
        Set(value As Boolean)
            _isChecked = value
            OnPropertyChanged("IsChecked")
        End Set
    End Property

    Private _uiVisibility As Visibility = Visibility.Visible
    Public Property UIVisibility As Visibility
        Get
            Return _uiVisibility
        End Get
        Set(value As Visibility)
            _uiVisibility = value
            OnPropertyChanged("UIVisibility")
        End Set
    End Property

    Public ReadOnly Property StatusText As String
        Get
            If IsMissing Then Return "Missing PDF"
            If IsDuplicate Then Return "(Duplicate)"
            If Not IsPDF Then Return "(Source)"
            Dim s = If(IsReleased, "Released", "WIP")
            If Not String.IsNullOrEmpty(Revision) Then
                Return s & " (Rev " & Revision & ")"
            End If
            Return s
        End Get
    End Property

    Public ReadOnly Property StatusColor As Brush
        Get
            If IsMissing Then Return Brushes.Red
            If IsDuplicate Then Return Brushes.Gray
            If Not IsPDF Then Return Brushes.LightBlue
            Return If(IsReleased, Brushes.White, Brushes.Orange)
        End Get
    End Property

    Public ReadOnly Property IsEnabled As Boolean
        Get
            Return IsPDF AndAlso Not IsMissing
        End Get
    End Property
End Class


' --- Web API v2 Backend (Zero Vault DLLs / Zero Install Required) ---
Public Class WebApiVaultBackend
    Implements IVaultBackend

    Private ReadOnly _origin As String
    Private ReadOnly _apiBase As String
    Private ReadOnly _vaultId As String
    Private ReadOnly _accessToken As String
    Private ReadOnly _connectedUser As String
    Private ReadOnly _usedFallback As Boolean
    Private ReadOnly _namePropDefUrl As String
    Private ReadOnly _http As HttpClient
    Private ReadOnly _json As JavaScriptSerializer
    Private ReadOnly _fileCache As New Dictionary(Of Long, VaultFileInfo)()
    Private _isConnected As Boolean

    Private Sub New(origin As String,
                    apiBase As String,
                    vaultId As String,
                    accessToken As String,
                    connectedUser As String,
                    usedFallback As Boolean,
                    namePropDefUrl As String,
                    http As HttpClient)
        _origin = origin
        _apiBase = apiBase
        _vaultId = vaultId
        _accessToken = accessToken
        _connectedUser = connectedUser
        _usedFallback = usedFallback
        _namePropDefUrl = namePropDefUrl
        _http = http
        _json = New JavaScriptSerializer()
        _json.MaxJsonLength = Integer.MaxValue
        _isConnected = True
    End Sub

    Public ReadOnly Property ModeName As String Implements IVaultBackend.ModeName
        Get
            Return If(_usedFallback, "Vault Web API v2 (Auto Fallback)", "Vault Web API v2")
        End Get
    End Property

    Public ReadOnly Property ShortBadge As String Implements IVaultBackend.ShortBadge
        Get
            Return If(_usedFallback, "Web API (Fallback)", "Web API")
        End Get
    End Property

    Public ReadOnly Property ConnectedUser As String Implements IVaultBackend.ConnectedUser
        Get
            Return _connectedUser
        End Get
    End Property

    Public ReadOnly Property ServerAddress As String Implements IVaultBackend.ServerAddress
        Get
            Return _origin
        End Get
    End Property

    Public ReadOnly Property IsConnected As Boolean Implements IVaultBackend.IsConnected
        Get
            Return _isConnected
        End Get
    End Property

    Public ReadOnly Property UsedFallback As Boolean Implements IVaultBackend.UsedFallback
        Get
            Return _usedFallback
        End Get
    End Property

    Public Shared Function Connect(server As String,
                                   vaultName As String,
                                   user As String,
                                   pass As String,
                                   useWindowsAuth As Boolean,
                                   usedFallback As Boolean) As WebApiVaultBackend
        ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol Or SecurityProtocolType.Tls12

        Dim origin = VaultBackendFactory.NormalizeOriginForWeb(server)
        Dim apiBase = origin & "/AutodeskDM/Services/api/vault/v2"

        Dim handler As New HttpClientHandler()
        handler.UseDefaultCredentials = useWindowsAuth
        Dim http As New HttpClient(handler)
        http.Timeout = TimeSpan.FromMinutes(3)

        Dim json As New JavaScriptSerializer()
        json.MaxJsonLength = Integer.MaxValue

        Try
            Dim inputMap As New Dictionary(Of String, Object)()
            inputMap("vault") = vaultName
            inputMap("appCode") = "TC"

            Dim sessionUrl As String
            If useWindowsAuth Then
                sessionUrl = apiBase & "/sessions/win-auth"
            Else
                inputMap("userName") = user
                inputMap("password") = pass
                sessionUrl = apiBase & "/sessions"
            End If

            Dim payloadMap As New Dictionary(Of String, Object)()
            payloadMap("input") = inputMap
            Dim payloadJson = json.Serialize(payloadMap)

            Dim sessionObj = SendJsonStatic(http, json, HttpMethod.Post, sessionUrl, payloadJson)
            Dim sessionDict = AsDict(sessionObj)
            If sessionDict Is Nothing Then
                Throw New Exception("Unexpected empty response from Vault Web API login.")
            End If

            Dim token = GetStr(sessionDict, "accessToken")
            If String.IsNullOrEmpty(token) Then
                Throw New Exception("Vault Web API login did not return an accessToken.")
            End If

            http.DefaultRequestHeaders.Authorization = New AuthenticationHeaderValue("Bearer", token)

            Dim vaultId As String = ""
            Dim vInfo = AsDict(GetVal(sessionDict, "vaultInformation"))
            If vInfo IsNot Nothing Then
                vaultId = GetStr(vInfo, "id")
                If String.IsNullOrEmpty(vaultId) Then vaultId = GetStr(vInfo, "vaultId")
            End If
            If String.IsNullOrEmpty(vaultId) Then
                vaultId = ResolveVaultIdByName(http, json, apiBase, vaultName)
            End If
            If String.IsNullOrEmpty(vaultId) Then
                Throw New Exception("Could not determine Vault ID for '" & vaultName & "'.")
            End If

            Dim connUser As String = ""
            Dim uInfo = AsDict(GetVal(sessionDict, "userInformation"))
            If uInfo IsNot Nothing Then
                connUser = GetStr(uInfo, "name")
                If String.IsNullOrEmpty(connUser) Then connUser = GetStr(uInfo, "userName")
            End If
            If String.IsNullOrEmpty(connUser) Then
                connUser = If(useWindowsAuth, Environment.UserName, user)
            End If

            Dim namePropUrl = ResolveNamePropertyDefUrl(http, json, apiBase, vaultId)

            Return New WebApiVaultBackend(origin, apiBase, vaultId, token, connUser, usedFallback, namePropUrl, http)
        Catch
            Try : http.Dispose() : Catch : End Try
            Throw
        End Try
    End Function

    Private Shared Function ResolveVaultIdByName(http As HttpClient, json As JavaScriptSerializer, apiBase As String, vaultName As String) As String
        Dim resp = AsDict(SendJsonStatic(http, json, HttpMethod.Get, apiBase & "/vaults", Nothing))
        Dim results = GetArr(resp, "results")
        If results Is Nothing Then Return ""
        For Each item In results
            Dim d = AsDict(item)
            If d Is Nothing Then Continue For
            If String.Equals(GetStr(d, "name"), vaultName, StringComparison.OrdinalIgnoreCase) Then
                Return GetStr(d, "id")
            End If
        Next
        If results.Count > 0 Then
            Dim firstD = AsDict(results(0))
            If firstD IsNot Nothing Then Return GetStr(firstD, "id")
        End If
        Return ""
    End Function

    Private Shared Function ResolveNamePropertyDefUrl(http As HttpClient, json As JavaScriptSerializer, apiBase As String, vaultId As String) As String
        Dim url = apiBase & "/vaults/" & Uri.EscapeDataString(vaultId) & "/property-definitions?filter[entityClassId]=FILE&filter[systemNames]=ClientFileName,Name"
        Dim resp = AsDict(SendJsonStatic(http, json, HttpMethod.Get, url, Nothing))
        Dim results = GetArr(resp, "results")
        If results IsNot Nothing Then
            Dim fallbackUrl As String = ""
            Dim fileNameUrl As String = ""
            For Each item In results
                Dim d = AsDict(item)
                If d Is Nothing Then Continue For
                Dim pUrl = GetStr(d, "url")
                If String.IsNullOrEmpty(pUrl) Then
                    Dim pId = GetStr(d, "id")
                    If Not String.IsNullOrEmpty(pId) Then
                        pUrl = "/AutodeskDM/Services/api/vault/v2/vaults/" & vaultId & "/property-definitions/" & pId
                    End If
                End If
                If String.IsNullOrEmpty(fallbackUrl) Then fallbackUrl = pUrl

                Dim sysName = GetStr(d, "systemName")
                Dim dispName = GetStr(d, "displayName")
                If String.Equals(sysName, "Name", StringComparison.OrdinalIgnoreCase) Then
                    Return pUrl
                End If
                If String.Equals(dispName, "File Name", StringComparison.OrdinalIgnoreCase) Then
                    fileNameUrl = pUrl
                End If
            Next
            If Not String.IsNullOrEmpty(fileNameUrl) Then Return fileNameUrl
            If Not String.IsNullOrEmpty(fallbackUrl) Then Return fallbackUrl
        End If
        Return "/AutodeskDM/Services/api/vault/v2/vaults/" & vaultId & "/property-definitions/35"
    End Function

    Public Function SearchFilesByName(term As String, Optional exactMatch As Boolean = False) As List(Of VaultFileInfo) Implements IVaultBackend.SearchFilesByName
        Dim results As New List(Of VaultFileInfo)()
        If Not _isConnected OrElse String.IsNullOrWhiteSpace(term) Then Return results

        Dim criteria As New Dictionary(Of String, Object)()
        criteria("propertyDefinitionUrl") = _namePropDefUrl
        criteria("operator") = If(exactMatch, "IsExactly", "Contains")
        criteria("searchString") = term

        Dim bodyMap As New Dictionary(Of String, Object)()
        bodyMap("entityTypesToSearch") = New String() {"File"}
        bodyMap("searchCriterias") = New Object() {criteria}
        Dim bodyJson = _json.Serialize(bodyMap)

        Dim nextUrl As String = _apiBase & "/vaults/" & Uri.EscapeDataString(_vaultId) & ":advanced-search?option[latestOnly]=true&option[releasedFilesOnly]=false&limit=100"
        Dim seenIds As New HashSet(Of Long)()
        Dim pageCount As Integer = 0

        While Not String.IsNullOrEmpty(nextUrl) AndAlso pageCount < 50
            pageCount += 1
            Dim resp = AsDict(SendJsonRequest(HttpMethod.Post, nextUrl, bodyJson))
            If resp Is Nothing Then Exit While

            Dim items = GetArr(resp, "results")
            If items IsNot Nothing Then
                For Each item In items
                    Dim info = ParseFileVersion(AsDict(item))
                    If info IsNot Nothing AndAlso info.Id > 0 AndAlso Not String.IsNullOrEmpty(info.Name) Then
                        If Not seenIds.Contains(info.Id) Then
                            seenIds.Add(info.Id)
                            results.Add(info)
                        End If
                    End If
                Next
            End If

            Dim pag = AsDict(GetVal(resp, "pagination"))
            Dim rawNext = GetStr(pag, "nextUrl")
            nextUrl = ResolveNextUrl(rawNext)
        End While

        Return results
    End Function

    Public Function GetFileDetails(fileId As Long) As VaultFileInfo Implements IVaultBackend.GetFileDetails
        If fileId <= 0 Then Return Nothing
        SyncLock _fileCache
            If _fileCache.ContainsKey(fileId) Then
                Return _fileCache(fileId)
            End If
        End SyncLock

        If Not _isConnected Then Return Nothing
        Try
            Dim url = _apiBase & "/vaults/" & Uri.EscapeDataString(_vaultId) & "/file-versions/" & fileId.ToString()
            Dim resp = AsDict(SendJsonRequest(HttpMethod.Get, url, Nothing))
            Return ParseFileVersion(resp)
        Catch
            Return Nothing
        End Try
    End Function

    Public Function GetDirectAttachments(fileId As Long) As List(Of VaultFileInfo) Implements IVaultBackend.GetDirectAttachments
        Dim results As New List(Of VaultFileInfo)()
        If Not _isConnected OrElse fileId <= 0 Then Return results

        Dim nextUrl As String = _apiBase & "/vaults/" & Uri.EscapeDataString(_vaultId) & "/file-versions/" & fileId.ToString() & "/uses?option[releaseBiased]=false&option[getLatestAssocs]=true&limit=100"
        Dim seenIds As New HashSet(Of Long)()
        Dim pageCount As Integer = 0

        While Not String.IsNullOrEmpty(nextUrl) AndAlso pageCount < 20
            pageCount += 1
            Dim resp = AsDict(SendJsonRequest(HttpMethod.Get, nextUrl, Nothing))
            If resp Is Nothing Then Exit While

            Dim items = GetArr(resp, "results")
            If items IsNot Nothing Then
                For Each item In items
                    Dim d = AsDict(item)
                    If d Is Nothing Then Continue For
                    Dim assocType = GetStr(d, "fileAssocType")
                    If String.Equals(assocType, "Attachment", StringComparison.OrdinalIgnoreCase) Then
                        Dim child = ParseFileVersion(AsDict(GetVal(d, "childFile")))
                        If child IsNot Nothing AndAlso child.Id > 0 AndAlso Not String.IsNullOrEmpty(child.Name) Then
                            If Not seenIds.Contains(child.Id) Then
                                seenIds.Add(child.Id)
                                results.Add(child)
                            End If
                        End If
                    End If
                Next
            End If

            Dim pag = AsDict(GetVal(resp, "pagination"))
            nextUrl = ResolveNextUrl(GetStr(pag, "nextUrl"))
        End While

        Return results
    End Function

    Public Function GetParentDependencies(fileId As Long) As List(Of VaultFileInfo) Implements IVaultBackend.GetParentDependencies
        Dim results As New List(Of VaultFileInfo)()
        If Not _isConnected OrElse fileId <= 0 Then Return results

        Dim nextUrl As String = _apiBase & "/vaults/" & Uri.EscapeDataString(_vaultId) & "/file-versions/" & fileId.ToString() & "/parents?option[releaseBiased]=false&option[getLatestAssocs]=true&limit=100"
        Dim seenIds As New HashSet(Of Long)()
        Dim pageCount As Integer = 0

        While Not String.IsNullOrEmpty(nextUrl) AndAlso pageCount < 20
            pageCount += 1
            Dim resp = AsDict(SendJsonRequest(HttpMethod.Get, nextUrl, Nothing))
            If resp Is Nothing Then Exit While

            Dim items = GetArr(resp, "results")
            If items IsNot Nothing Then
                For Each item In items
                    Dim d = AsDict(item)
                    If d Is Nothing Then Continue For
                    Dim assocType = GetStr(d, "fileAssocType")
                    If String.Equals(assocType, "Dependency", StringComparison.OrdinalIgnoreCase) Then
                        Dim parent = ParseFileVersion(AsDict(GetVal(d, "parentFile")))
                        If parent IsNot Nothing AndAlso parent.Id > 0 AndAlso Not String.IsNullOrEmpty(parent.Name) Then
                            If Not seenIds.Contains(parent.Id) Then
                                seenIds.Add(parent.Id)
                                results.Add(parent)
                            End If
                        End If
                    End If
                Next
            End If

            Dim pag = AsDict(GetVal(resp, "pagination"))
            nextUrl = ResolveNextUrl(GetStr(pag, "nextUrl"))
        End While

        Return results
    End Function

    Public Function DownloadFile(fileId As Long, fileName As String, destDir As String, Optional forTempParse As Boolean = False) As String Implements IVaultBackend.DownloadFile
        If Not _isConnected OrElse fileId <= 0 Then Return Nothing

        If Not Directory.Exists(destDir) Then Directory.CreateDirectory(destDir)

        Dim resolvedName = fileName
        If String.IsNullOrWhiteSpace(resolvedName) Then
            Dim det = GetFileDetails(fileId)
            If det IsNot Nothing Then resolvedName = det.Name
        End If
        If String.IsNullOrWhiteSpace(resolvedName) Then
            resolvedName = fileId.ToString() & ".bin"
        End If

        Dim safeName = Path.GetFileName(resolvedName)
        Dim targetPath = Path.Combine(destDir, safeName)
        If File.Exists(targetPath) Then File.Delete(targetPath)

        Dim contentUrl = _apiBase & "/vaults/" & Uri.EscapeDataString(_vaultId) & "/file-versions/" & fileId.ToString() & "/content?allowSync=true"

        Dim httpClient = _http
        Task.Run(Async Function() As Task
                     Using req As New HttpRequestMessage(HttpMethod.Get, contentUrl)
                         Using resp = Await httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(False)
                             If Not resp.IsSuccessStatusCode Then
                                 Dim errBody = Await resp.Content.ReadAsStringAsync().ConfigureAwait(False)
                                 Throw New Exception(FormatHttpError(CInt(resp.StatusCode), resp.ReasonPhrase, errBody, _json))
                             End If
                             Using srcStream = Await resp.Content.ReadAsStreamAsync().ConfigureAwait(False)
                                 Using fs As New FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, True)
                                     Await srcStream.CopyToAsync(fs).ConfigureAwait(False)
                                 End Using
                             End Using
                         End Using
                     End Using
                 End Function).GetAwaiter().GetResult()

        Return targetPath
    End Function

    Public Sub Disconnect() Implements IVaultBackend.Disconnect
        If Not _isConnected Then Return
        _isConnected = False
        Try
            Dim url = _apiBase & "/sessions/@current"
            Dim httpClient = _http
            Task.Run(Async Function() As Task
                         Using req As New HttpRequestMessage(HttpMethod.Delete, url)
                             Using resp = Await httpClient.SendAsync(req).ConfigureAwait(False)
                             End Using
                         End Using
                     End Function).GetAwaiter().GetResult()
        Catch
        End Try
        Try
            _http.Dispose()
        Catch
        End Try
    End Sub

    Private Function ResolveNextUrl(rawNext As String) As String
        If String.IsNullOrWhiteSpace(rawNext) Then Return Nothing
        If rawNext.StartsWith("http://", StringComparison.OrdinalIgnoreCase) OrElse
           rawNext.StartsWith("https://", StringComparison.OrdinalIgnoreCase) Then
            Return rawNext
        End If
        If Not rawNext.StartsWith("/") Then rawNext = "/" & rawNext
        Return _origin & rawNext
    End Function

    Private Function SendJsonRequest(method As HttpMethod, url As String, jsonBody As String) As Object
        Return SendJsonStatic(_http, _json, method, url, jsonBody)
    End Function

    Private Shared Function SendJsonStatic(httpClient As HttpClient,
                                           serializer As JavaScriptSerializer,
                                           method As HttpMethod,
                                           url As String,
                                           jsonBody As String) As Object
        Dim rawText As String = Task.Run(Async Function() As Task(Of String)
                                             Using req As New HttpRequestMessage(method, url)
                                                 req.Headers.Accept.Add(New MediaTypeWithQualityHeaderValue("application/json"))
                                                 If jsonBody IsNot Nothing Then
                                                     req.Content = New StringContent(jsonBody, Encoding.UTF8, "application/json")
                                                 End If
                                                 Using resp = Await httpClient.SendAsync(req).ConfigureAwait(False)
                                                     Dim bodyText = Await resp.Content.ReadAsStringAsync().ConfigureAwait(False)
                                                     If Not resp.IsSuccessStatusCode Then
                                                         Throw New Exception(FormatHttpError(CInt(resp.StatusCode), resp.ReasonPhrase, bodyText, serializer))
                                                     End If
                                                     Return bodyText
                                                 End Using
                                             End Using
                                         End Function).GetAwaiter().GetResult()

        If String.IsNullOrWhiteSpace(rawText) Then Return Nothing
        Return serializer.DeserializeObject(rawText)
    End Function

    Private Shared Function FormatHttpError(statusCode As Integer, reason As String, bodyText As String, serializer As JavaScriptSerializer) As String
        Dim detail As String = ""
        Try
            If Not String.IsNullOrWhiteSpace(bodyText) Then
                Dim errDict = AsDict(serializer.DeserializeObject(bodyText))
                If errDict IsNot Nothing Then
                    detail = GetStr(errDict, "detail")
                    If String.IsNullOrEmpty(detail) Then detail = GetStr(errDict, "message")
                    If String.IsNullOrEmpty(detail) Then detail = GetStr(errDict, "title")
                End If
            End If
        Catch
        End Try
        If String.IsNullOrEmpty(detail) Then detail = If(String.IsNullOrEmpty(bodyText), reason, bodyText)
        Return String.Format("Vault Web API Error ({0}): {1}", statusCode, detail)
    End Function

    Private Function ParseFileVersion(dict As Dictionary(Of String, Object)) As VaultFileInfo
        If dict Is Nothing Then Return Nothing
        Dim isCloaked As Boolean = False
        Dim clkObj = GetVal(dict, "isCloaked")
        If clkObj IsNot Nothing Then
            Try : isCloaked = Convert.ToBoolean(clkObj) : Catch : End Try
        End If
        If isCloaked Then Return Nothing

        Dim idVal = GetLong(dict, "id")
        If idVal <= 0 Then Return Nothing

        Dim info As New VaultFileInfo() With {
            .Id = idVal,
            .Name = GetStr(dict, "name"),
            .State = GetStr(dict, "state"),
            .Revision = GetStr(dict, "revision"),
            .Size = GetLong(dict, "size")
        }

        SyncLock _fileCache
            _fileCache(info.Id) = info
        End SyncLock

        Return info
    End Function

    Private Shared Function AsDict(obj As Object) As Dictionary(Of String, Object)
        Return TryCast(obj, Dictionary(Of String, Object))
    End Function

    Private Shared Function GetVal(dict As Dictionary(Of String, Object), key As String) As Object
        If dict Is Nothing Then Return Nothing
        If dict.ContainsKey(key) Then Return dict(key)
        Return Nothing
    End Function

    Private Shared Function GetStr(dict As Dictionary(Of String, Object), key As String) As String
        Dim v = GetVal(dict, key)
        If v Is Nothing Then Return ""
        Return v.ToString().Trim()
    End Function

    Private Shared Function GetLong(dict As Dictionary(Of String, Object), key As String) As Long
        Dim v = GetVal(dict, key)
        If v Is Nothing Then Return 0L
        If TypeOf v Is Long Then Return DirectCast(v, Long)
        If TypeOf v Is Integer Then Return CLng(DirectCast(v, Integer))
        Dim parsed As Long = 0L
        If Long.TryParse(v.ToString(), parsed) Then Return parsed
        Return 0L
    End Function

    Private Shared Function GetArr(dict As Dictionary(Of String, Object), key As String) As System.Collections.ArrayList
        Dim v = GetVal(dict, key)
        If v Is Nothing Then Return Nothing
        Dim al = TryCast(v, System.Collections.ArrayList)
        If al IsNot Nothing Then Return al
        Dim objArr = TryCast(v, Object())
        If objArr IsNot Nothing Then Return New System.Collections.ArrayList(objArr)
        Return Nothing
    End Function
End Class


' --- Local Vault Client 2025 Backend (Runtime Reflection Only) ---
Public Class LocalClientVaultBackend
    Implements IVaultBackend

    Private Const VaultClientPath2025 As String = "C:\Program Files\Autodesk\Vault Client 2025\Explorer"
    Private Shared ReadOnly RequiredDlls As String() = {
        "Autodesk.Connectivity.WebServices.dll",
        "Autodesk.DataManagement.Client.Framework.dll",
        "Autodesk.DataManagement.Client.Framework.Vault.dll"
    }

    Private ReadOnly _vdfConn As Object
    Private ReadOnly _wsm As Object
    Private ReadOnly _serverHost As String
    Private ReadOnly _connectedUser As String
    Private ReadOnly _namePropDefId As Long
    Private ReadOnly _fileCache As New Dictionary(Of Long, VaultFileInfo)()
    Private _isConnected As Boolean

    Private Sub New(vdfConn As Object, wsm As Object, serverHost As String, connectedUser As String, namePropDefId As Long)
        _vdfConn = vdfConn
        _wsm = wsm
        _serverHost = serverHost
        _connectedUser = connectedUser
        _namePropDefId = namePropDefId
        _isConnected = True
    End Sub

    Public Shared Function IsAvailable() As Boolean
        Try
            If Not Directory.Exists(VaultClientPath2025) Then Return False
            For Each dll In RequiredDlls
                If Not File.Exists(Path.Combine(VaultClientPath2025, dll)) Then Return False
            Next
            Return True
        Catch
            Return False
        End Try
    End Function

    Public ReadOnly Property ModeName As String Implements IVaultBackend.ModeName
        Get
            Return "Local Vault Client 2025"
        End Get
    End Property

    Public ReadOnly Property ShortBadge As String Implements IVaultBackend.ShortBadge
        Get
            Return "Local Client"
        End Get
    End Property

    Public ReadOnly Property ConnectedUser As String Implements IVaultBackend.ConnectedUser
        Get
            Return _connectedUser
        End Get
    End Property

    Public ReadOnly Property ServerAddress As String Implements IVaultBackend.ServerAddress
        Get
            Return _serverHost
        End Get
    End Property

    Public ReadOnly Property IsConnected As Boolean Implements IVaultBackend.IsConnected
        Get
            Return _isConnected
        End Get
    End Property

    Public ReadOnly Property UsedFallback As Boolean Implements IVaultBackend.UsedFallback
        Get
            Return False
        End Get
    End Property

    Public Shared Function Connect(server As String,
                                   vaultName As String,
                                   user As String,
                                   pass As String,
                                   useWindowsAuth As Boolean) As LocalClientVaultBackend
        If Not IsAvailable() Then
            Throw New VaultComponentUnavailableException("Vault Client 2025 path or required DLLs not found at: " & VaultClientPath2025)
        End If

        For Each dll In RequiredDlls
            Try
                Assembly.LoadFrom(Path.Combine(VaultClientPath2025, dll))
            Catch ex As Exception
                Throw New VaultComponentUnavailableException("Failed to load Vault Client DLL '" & dll & "': " & ex.Message, ex)
            End Try
        Next

        Dim siType = FindAutodeskType("ServerIdentities")
        If siType Is Nothing Then
            Throw New VaultComponentUnavailableException("Could not find ServerIdentities type in Vault Client assemblies.")
        End If

        Dim host = VaultBackendFactory.NormalizeHostForLocal(server)
        Dim si = Activator.CreateInstance(siType)
        siType.GetProperty("DataServer").SetValue(si, host, Nothing)
        siType.GetProperty("FileServer").SetValue(si, host, Nothing)

        Dim creds As Object = Nothing
        If useWindowsAuth Then
            Dim winCredType = FindAutodeskType("WinAuthCredentials")
            If winCredType Is Nothing Then
                Throw New VaultComponentUnavailableException("Could not find WinAuthCredentials type.")
            End If
            For Each c In winCredType.GetConstructors()
                Dim ps = c.GetParameters()
                If ps.Length = 4 AndAlso ps(0).ParameterType Is siType AndAlso
                   ps(1).ParameterType Is GetType(String) AndAlso
                   ps(2).ParameterType Is GetType(Boolean) AndAlso
                   ps(3).ParameterType Is GetType(NetworkCredential) Then
                    creds = c.Invoke(New Object() {si, vaultName, True, Nothing})
                    Exit For
                End If
            Next
            If creds Is Nothing Then
                Throw New VaultComponentUnavailableException("Could not find matching WinAuthCredentials constructor.")
            End If
        Else
            Dim credType = FindAutodeskType("UserPasswordCredentials")
            If credType Is Nothing Then
                Throw New VaultComponentUnavailableException("Could not find UserPasswordCredentials type.")
            End If
            For Each c In credType.GetConstructors()
                Dim ps = c.GetParameters()
                If ps.Length = 5 AndAlso ps(0).ParameterType Is siType AndAlso
                   ps(1).ParameterType Is GetType(String) AndAlso
                   ps(2).ParameterType Is GetType(String) AndAlso
                   ps(3).ParameterType Is GetType(String) AndAlso
                   ps(4).ParameterType Is GetType(Boolean) Then
                    creds = c.Invoke(New Object() {si, vaultName, user, pass, True})
                    Exit For
                End If
            Next
            If creds Is Nothing Then
                For Each c In credType.GetConstructors()
                    Dim ps = c.GetParameters()
                    If ps.Length = 5 Then
                        Try
                            creds = c.Invoke(New Object() {si, vaultName, user, pass, True})
                            Exit For
                        Catch
                        End Try
                        Try
                            creds = c.Invoke(New Object() {si, vaultName, user, pass, 0})
                            Exit For
                        Catch
                        End Try
                    End If
                Next
            End If
            If creds Is Nothing Then
                Throw New VaultComponentUnavailableException("Could not find matching UserPasswordCredentials constructor.")
            End If
        End If

        Dim wsmType = FindAutodeskType("WebServiceManager")
        If wsmType Is Nothing Then
            Throw New VaultComponentUnavailableException("Could not find WebServiceManager type.")
        End If

        Dim wsm As Object = Nothing
        Dim vdfConn As Object = Nothing
        Try
            wsm = Activator.CreateInstance(wsmType, New Object() {creds})

            Dim connType As Type = Nothing
            For Each a In AppDomain.CurrentDomain.GetAssemblies()
                If a.FullName.Contains("Autodesk.DataManagement.Client.Framework.Vault") Then
                    connType = a.GetType("Autodesk.DataManagement.Client.Framework.Vault.Currency.Connections.Connection")
                    If connType IsNot Nothing Then Exit For
                End If
            Next
            If connType Is Nothing Then
                Throw New VaultComponentUnavailableException("Could not find VDF Connection type.")
            End If

            For Each c As ConstructorInfo In connType.GetConstructors()
                Dim ps = c.GetParameters()
                If ps.Length >= 1 AndAlso ps(0).ParameterType.Name.Contains("WebServiceManager") Then
                    Dim cArgs(ps.Length - 1) As Object
                    cArgs(0) = wsm
                    For i As Integer = 1 To ps.Length - 1
                        Dim pType As Type = ps(i).ParameterType
                        If pType Is GetType(String) Then
                            cArgs(i) = String.Empty
                            If ps.Length = 5 Then
                                If i = 1 Then cArgs(i) = If(useWindowsAuth, Environment.UserName, user)
                                If i = 3 Then cArgs(i) = vaultName
                            ElseIf ps.Length = 7 Then
                                If i = 1 Then cArgs(i) = If(useWindowsAuth, Environment.UserName, user)
                                If i = 2 Then cArgs(i) = If(useWindowsAuth, "", pass)
                                If i = 3 Then cArgs(i) = vaultName
                                If i = 5 Then cArgs(i) = vaultName
                            End If
                        ElseIf pType Is GetType(Long) OrElse pType Is GetType(Int64) Then
                            cArgs(i) = 0L
                        ElseIf pType.IsEnum OrElse pType Is GetType(Integer) Then
                            cArgs(i) = 0
                        End If
                    Next
                    Try
                        vdfConn = c.Invoke(cArgs)
                        Exit For
                    Catch
                    End Try
                End If
            Next

            If vdfConn Is Nothing Then
                Throw New VaultComponentUnavailableException("Could not find suitable VDF Connection constructor.")
            End If

            ' Force authentication handshake immediately and cache Name PropDefId
            Dim propDefId = ResolveNamePropDefId(wsm)

            Dim connUser = If(useWindowsAuth, Environment.UserName, user)
            Try
                Dim vdfUser = CStr(GetProp(vdfConn, "UserName"))
                If Not String.IsNullOrWhiteSpace(vdfUser) Then connUser = vdfUser
            Catch
            End Try

            Return New LocalClientVaultBackend(vdfConn, wsm, host, connUser, propDefId)
        Catch ex As VaultComponentUnavailableException
            DisposeQuietly(wsm)
            Throw
        Catch ex As Exception
            DisposeQuietly(wsm)
            Dim realEx = UnwrapException(ex)
            If IsMissingAutodeskComponent(realEx) Then
                Throw New VaultComponentUnavailableException("Local Vault Client component failure: " & realEx.Message, realEx)
            End If
            Throw New Exception(realEx.Message, realEx)
        End Try
    End Function

    Private Shared Function UnwrapException(ex As Exception) As Exception
        Dim cur = ex
        While TypeOf cur Is TargetInvocationException AndAlso cur.InnerException IsNot Nothing
            cur = cur.InnerException
        End While
        Return cur
    End Function

    Private Shared Function IsMissingAutodeskComponent(ex As Exception) As Boolean
        If ex Is Nothing Then Return False
        If TypeOf ex Is FileNotFoundException OrElse TypeOf ex Is FileLoadException OrElse
           TypeOf ex Is BadImageFormatException OrElse TypeOf ex Is TypeLoadException Then
            Return True
        End If
        If ex.InnerException IsNot Nothing Then Return IsMissingAutodeskComponent(ex.InnerException)
        Return False
    End Function

    Private Shared Sub DisposeQuietly(obj As Object)
        If obj IsNot Nothing AndAlso TypeOf obj Is IDisposable Then
            Try : DirectCast(obj, IDisposable).Dispose() : Catch : End Try
        End If
    End Sub

    Private Shared Function FindAutodeskType(name As String) As Type
        For Each a In AppDomain.CurrentDomain.GetAssemblies()
            Dim t = a.GetType("Autodesk.Connectivity.WebServicesTools." & name)
            If t Is Nothing Then t = a.GetType("Autodesk.Connectivity.WebServices.Tools." & name)
            If t IsNot Nothing Then Return t
        Next
        For Each a In AppDomain.CurrentDomain.GetAssemblies()
            If Not a.FullName.StartsWith("Autodesk") Then Continue For
            Try
                For Each t In a.GetTypes()
                    If t.Name = name Then Return t
                Next
            Catch
            End Try
        Next
        Return Nothing
    End Function

    Private Shared Function ResolveNamePropDefId(wsm As Object) As Long
        Dim propSvc = GetProp(wsm, "PropertyService")
        Dim getPropDefs = propSvc.GetType().GetMethod("GetPropertyDefinitionsByEntityClassId")
        If getPropDefs Is Nothing Then
            Throw New VaultComponentUnavailableException("PropertyService.GetPropertyDefinitionsByEntityClassId method not found.")
        End If
        Dim defs = InvokeMethod(propSvc, "GetPropertyDefinitionsByEntityClassId", New Object() {"FILE"})
        Dim arr = DirectCast(defs, Array)
        If arr IsNot Nothing Then
            For Each defObj As Object In arr
                Dim sysName = CStr(GetProp(defObj, "SysName"))
                Dim dispName = CStr(GetProp(defObj, "DispName"))
                If sysName = "Name" OrElse dispName = "File Name" Then
                    Return CLng(GetProp(defObj, "Id"))
                End If
            Next
        End If
        Return 15L
    End Function

    Public Function SearchFilesByName(term As String, Optional exactMatch As Boolean = False) As List(Of VaultFileInfo) Implements IVaultBackend.SearchFilesByName
        Dim results As New List(Of VaultFileInfo)()
        If Not _isConnected OrElse String.IsNullOrWhiteSpace(term) Then Return results

        Dim docSvc = GetProp(_wsm, "DocumentService")
        Dim findMethod As MethodInfo = Nothing
        For Each mi As MethodInfo In docSvc.GetType().GetMethods()
            If mi.Name = "FindFilesBySearchConditions" Then
                findMethod = mi
                Exit For
            End If
        Next
        If findMethod Is Nothing Then Return results

        Dim params As ParameterInfo() = findMethod.GetParameters()
        Dim srchCondType As Type = params(0).ParameterType.GetElementType()

        Dim cond As Object = Activator.CreateInstance(srchCondType)
        SetPropSafe(cond, "PropDefId", _namePropDefId)
        SetPropSafe(cond, "SrchOper", If(exactMatch, 3, 1)) ' 3 = Equals, 1 = Contains
        SetPropSafe(cond, "SrchTxt", term)
        SetPropSafe(cond, "PropTyp", "SingleProperty")
        SetPropSafe(cond, "SrchRule", "Must")

        Dim conditions As Array = Array.CreateInstance(srchCondType, 1)
        conditions.SetValue(cond, 0)

        Dim args(params.Length - 1) As Object
        args(0) = conditions

        Dim bookmarkIndex As Integer = -1
        For i As Integer = 1 To params.Length - 1
            Dim p As ParameterInfo = params(i)
            If p.IsOut OrElse p.ParameterType.IsByRef Then
                If p.ParameterType.FullName.Contains("String") Then
                    args(i) = ""
                    bookmarkIndex = i
                Else
                    args(i) = Nothing
                End If
            ElseIf p.ParameterType.IsArray Then
                args(i) = Nothing
            ElseIf p.ParameterType = GetType(Boolean) Then
                args(i) = True
            Else
                Try : args(i) = Activator.CreateInstance(p.ParameterType) : Catch : args(i) = Nothing : End Try
            End If
        Next

        Dim seenIds As New HashSet(Of Long)()
        Dim limitBreak As Integer = 0
        Do
            Dim result = findMethod.Invoke(docSvc, args)
            If result IsNot Nothing Then
                Dim arr As Array = DirectCast(result, Array)
                For Each item As Object In arr
                    Dim info = MapVaultFile(item)
                    If info IsNot Nothing AndAlso info.Id > 0 AndAlso Not seenIds.Contains(info.Id) Then
                        seenIds.Add(info.Id)
                        results.Add(info)
                    End If
                Next
            End If

            Dim bookmark As String = ""
            If bookmarkIndex > 0 AndAlso args(bookmarkIndex) IsNot Nothing Then
                bookmark = CStr(args(bookmarkIndex))
            End If
            If String.IsNullOrEmpty(bookmark) Then Exit Do

            limitBreak += 1
            If limitBreak > 50 Then Exit Do
        Loop

        Return results
    End Function

    Public Function GetFileDetails(fileId As Long) As VaultFileInfo Implements IVaultBackend.GetFileDetails
        If fileId <= 0 Then Return Nothing
        SyncLock _fileCache
            If _fileCache.ContainsKey(fileId) AndAlso Not String.IsNullOrEmpty(_fileCache(fileId).State) Then
                Return _fileCache(fileId)
            End If
        End SyncLock

        If Not _isConnected Then Return Nothing
        Try
            Dim docSvc = GetProp(_wsm, "DocumentService")
            Dim res = InvokeMethod(docSvc, "GetLatestFilesByIds", New Object() {New Long() {fileId}})
            Dim arr As Array = DirectCast(res, Array)
            If arr IsNot Nothing AndAlso arr.Length > 0 AndAlso arr.GetValue(0) IsNot Nothing Then
                Return MapVaultFile(arr.GetValue(0))
            End If
        Catch
        End Try
        Return Nothing
    End Function

    Public Function GetDirectAttachments(fileId As Long) As List(Of VaultFileInfo) Implements IVaultBackend.GetDirectAttachments
        Dim results As New List(Of VaultFileInfo)()
        If Not _isConnected OrElse fileId <= 0 Then Return results

        Dim docSvc = GetProp(_wsm, "DocumentService")
        Dim method = docSvc.GetType().GetMethod("GetFileAssociationLitesByIds")
        If method Is Nothing Then Return results

        Dim ps = method.GetParameters()
        If ps.Length <> 9 Then Return results

        Dim alg = [Enum].ToObject(ps(1).ParameterType, 1) ' LatestTip
        Dim typeNone = [Enum].ToObject(ps(2).ParameterType, 0)
        Dim typeAttach = [Enum].ToObject(ps(4).ParameterType, 1) ' Attachment
        Dim args = New Object() {New Long() {fileId}, alg, typeNone, False, typeAttach, False, False, True, False}

        Dim res = method.Invoke(docSvc, args)
        If res Is Nothing Then Return results

        Dim childIds As New List(Of Long)()
        Dim seenIds As New HashSet(Of Long)()
        For Each item In DirectCast(res, Array)
            Dim cId As Long = CLng(GetProp(item, "CldFileId"))
            If cId > 0 AndAlso Not seenIds.Contains(cId) Then
                seenIds.Add(cId)
                childIds.Add(cId)
            End If
        Next

        Return FetchFilesByIds(childIds)
    End Function

    Public Function GetParentDependencies(fileId As Long) As List(Of VaultFileInfo) Implements IVaultBackend.GetParentDependencies
        Dim results As New List(Of VaultFileInfo)()
        If Not _isConnected OrElse fileId <= 0 Then Return results

        Dim docSvc = GetProp(_wsm, "DocumentService")
        Dim method = docSvc.GetType().GetMethod("GetFileAssociationLitesByIds")
        If method Is Nothing Then Return results

        Dim ps = method.GetParameters()
        If ps.Length <> 9 Then Return results

        Dim alg = [Enum].ToObject(ps(1).ParameterType, 1) ' LatestTip
        Dim typeNone = [Enum].ToObject(ps(2).ParameterType, 0)
        Dim typeDep = [Enum].ToObject(ps(4).ParameterType, 3) ' Dependency
        Dim args = New Object() {New Long() {fileId}, alg, typeDep, False, typeNone, False, False, False, False}

        Dim res = method.Invoke(docSvc, args)
        If res Is Nothing Then Return results

        Dim parentIds As New List(Of Long)()
        Dim seenIds As New HashSet(Of Long)()
        For Each item In DirectCast(res, Array)
            Dim pId As Long = CLng(GetProp(item, "ParFileId"))
            If pId > 0 AndAlso Not seenIds.Contains(pId) Then
                seenIds.Add(pId)
                parentIds.Add(pId)
            End If
        Next

        Return FetchFilesByIds(parentIds)
    End Function

    Private Function FetchFilesByIds(ids As List(Of Long)) As List(Of VaultFileInfo)
        Dim list As New List(Of VaultFileInfo)()
        If ids Is Nothing OrElse ids.Count = 0 Then Return list
        Try
            Dim docSvc = GetProp(_wsm, "DocumentService")
            Dim res = InvokeMethod(docSvc, "GetLatestFilesByIds", New Object() {ids.ToArray()})
            If res IsNot Nothing Then
                For Each fObj In DirectCast(res, Array)
                    If fObj Is Nothing Then Continue For
                    Dim info = MapVaultFile(fObj)
                    If info IsNot Nothing AndAlso info.Id > 0 Then list.Add(info)
                Next
            End If
        Catch
        End Try
        Return list
    End Function

    Public Function DownloadFile(fileId As Long, fileName As String, destDir As String, Optional forTempParse As Boolean = False) As String Implements IVaultBackend.DownloadFile
        If Not _isConnected OrElse fileId <= 0 Then Return Nothing
        If Not Directory.Exists(destDir) Then Directory.CreateDirectory(destDir)

        If forTempParse Then
            Dim tempPath = DownloadViaFilestore(fileId, fileName, destDir)
            If Not String.IsNullOrEmpty(tempPath) AndAlso File.Exists(tempPath) Then
                Return tempPath
            End If
        End If

        Return DownloadViaVdfAcquire(fileId, destDir)
    End Function

    Private Function DownloadViaFilestore(fileId As Long, fileName As String, destDir As String) As String
        Try
            Dim docSvc = GetProp(_wsm, "DocumentService")
            Dim res = InvokeMethod(docSvc, "GetLatestFilesByIds", New Object() {New Long() {fileId}})
            Dim arr As Array = DirectCast(res, Array)
            If arr Is Nothing OrElse arr.Length = 0 OrElse arr.GetValue(0) Is Nothing Then Return Nothing
            Dim fileObj = arr.GetValue(0)

            Dim actualName = CStr(GetProp(fileObj, "Name"))
            If String.IsNullOrEmpty(actualName) Then actualName = fileName
            Dim safeName = Path.GetFileName(actualName)
            Dim targetPath = Path.Combine(destDir, safeName)
            If File.Exists(targetPath) Then File.Delete(targetPath)

            Dim acqMethod = docSvc.GetType().GetMethod("GetDownloadTicketsByFileIds")
            If acqMethod Is Nothing Then Return Nothing

            Dim tickets = acqMethod.Invoke(docSvc, New Object() {New Long() {fileId}})
            If tickets Is Nothing Then Return Nothing

            Dim ticketArr As Array = DirectCast(tickets, Array)
            If ticketArr.Length = 0 Then Return Nothing

            Dim ticket = ticketArr.GetValue(0)
            Dim ticketBytes As Byte() = DirectCast(GetProp(ticket, "Bytes"), Byte())

            Dim filestoreSvc = GetProp(_wsm, "FilestoreService")
            Dim dlMethod = filestoreSvc.GetType().GetMethod("DownloadFilePart")
            If dlMethod Is Nothing Then Return Nothing

            Dim fileSize As Long = CLng(GetProp(fileObj, "FileSize"))
            If fileSize <= 0 Then fileSize = 50000000L

            Dim dlResult = dlMethod.Invoke(filestoreSvc, New Object() {ticketBytes, 0L, fileSize, True})
            If dlResult Is Nothing Then Return Nothing

            Using ms As New MemoryStream()
                Dim stream As Stream = DirectCast(dlResult, Stream)
                stream.CopyTo(ms)
                File.WriteAllBytes(targetPath, ms.ToArray())
                stream.Close()
            End Using
            Return targetPath
        Catch
            Return Nothing
        End Try
    End Function

    Private Function DownloadViaVdfAcquire(fileId As Long, destDir As String) As String
        Dim fm = GetProp(_vdfConn, "FileManager")

        Dim findVdfType = Function(typeName As String) As Type
                              For Each a In AppDomain.CurrentDomain.GetAssemblies()
                                  If a.FullName.Contains("Autodesk") Then
                                      Dim t = a.GetType(typeName)
                                      If t IsNot Nothing Then Return t
                                  End If
                              Next
                              Return Nothing
                          End Function

        Dim fIterType = findVdfType("Autodesk.DataManagement.Client.Framework.Vault.Currency.Entities.FileIteration")
        Dim settingsType = findVdfType("Autodesk.DataManagement.Client.Framework.Vault.Settings.AcquireFilesSettings")
        Dim fpType = findVdfType("Autodesk.DataManagement.Client.Framework.Currency.FolderPathAbsolute")
        If fIterType Is Nothing OrElse settingsType Is Nothing OrElse fpType Is Nothing Then
            Throw New Exception("VDF types missing for AcquireFiles.")
        End If

        Dim ds = GetProp(_wsm, "DocumentService")
        Dim fArr = InvokeMethod(ds, "GetLatestFilesByIds", New Object() {New Long() {fileId}})
        Dim fileObj = DirectCast(fArr, Array).GetValue(0)

        Dim fIter = Activator.CreateInstance(fIterType, New Object() {_vdfConn, fileObj})
        Dim settings = Activator.CreateInstance(settingsType, New Object() {_vdfConn, False})

        Dim propMeta = settings.GetType().GetProperty("CreateMetaDataFile")
        If propMeta IsNot Nothing Then
            propMeta.SetValue(settings, False, Nothing)
        End If

        For Each m In settings.GetType().GetMethods()
            If m.Name = "AddFileToAcquire" AndAlso m.GetParameters().Length = 2 Then
                Dim p2 = m.GetParameters()(1).ParameterType
                If p2.Name.Contains("AcquisitionOption") Then
                    Dim opt = [Enum].ToObject(p2, 1) ' Download=1
                    m.Invoke(settings, New Object() {fIter, opt})
                    Exit For
                End If
            End If
        Next

        Dim fp = Activator.CreateInstance(fpType, New Object() {destDir})
        settings.GetType().GetProperty("LocalPath").SetValue(settings, fp, Nothing)

        InvokeMethod(fm, "AcquireFiles", New Object() {settings})

        Dim name = CStr(GetProp(fileObj, "Name"))
        Return Path.Combine(destDir, name)
    End Function

    Public Sub Disconnect() Implements IVaultBackend.Disconnect
        If Not _isConnected Then Return
        _isConnected = False
        DisposeQuietly(_wsm)
    End Sub

    Private Function MapVaultFile(fileObj As Object) As VaultFileInfo
        If fileObj Is Nothing Then Return Nothing
        Dim idVal As Long = 0L
        Try : idVal = CLng(GetProp(fileObj, "Id")) : Catch : End Try
        If idVal <= 0 Then Return Nothing

        Dim nameVal = CStr(GetProp(fileObj, "Name"))
        If nameVal Is Nothing Then nameVal = ""

        Dim stateVal As String = ""
        Dim lfc = GetProp(fileObj, "FileLfCyc")
        If lfc IsNot Nothing Then
            Dim s = CStr(GetProp(lfc, "LfCycStateName"))
            If s IsNot Nothing Then stateVal = s.Trim()
        End If

        Dim revVal As String = ""
        Dim frev = GetProp(fileObj, "FileRev")
        If frev IsNot Nothing Then
            Dim lbl = CStr(GetProp(frev, "Label"))
            If lbl IsNot Nothing Then revVal = lbl.Trim()
        End If

        Dim sizeVal As Long = 0L
        Try
            Dim sz = GetProp(fileObj, "FileSize")
            If sz IsNot Nothing Then sizeVal = CLng(sz)
        Catch
        End Try

        Dim info As New VaultFileInfo() With {
            .Id = idVal,
            .Name = nameVal,
            .State = stateVal,
            .Revision = revVal,
            .Size = sizeVal
        }

        SyncLock _fileCache
            If Not _fileCache.ContainsKey(info.Id) OrElse Not String.IsNullOrEmpty(info.State) Then
                _fileCache(info.Id) = info
            End If
        End SyncLock

        Return info
    End Function

    Private Shared Function GetProp(obj As Object, prop As String) As Object
        If obj Is Nothing Then Return Nothing
        Dim p = obj.GetType().GetProperty(prop)
        If p IsNot Nothing Then Return p.GetValue(obj, Nothing)
        Return Nothing
    End Function

    Private Shared Function InvokeMethod(obj As Object, method As String, params As Object()) As Object
        If obj Is Nothing Then Return Nothing
        Dim m = obj.GetType().GetMethod(method)
        If m IsNot Nothing Then
            Try
                Return m.Invoke(obj, params)
            Catch ex As TargetInvocationException
                If ex.InnerException IsNot Nothing Then Throw ex.InnerException
                Throw
            End Try
        End If
        Return Nothing
    End Function

    Private Shared Sub SetPropSafe(obj As Object, prop As String, val As Object)
        Try
            Dim p = obj.GetType().GetProperty(prop)
            If p.PropertyType.IsEnum Then
                p.SetValue(obj, [Enum].ToObject(p.PropertyType, val), Nothing)
            Else
                p.SetValue(obj, val, Nothing)
            End If
        Catch
        End Try
    End Sub
End Class


' --- Pure PDF-Driven BOM Extraction Engine (Backend-Agnostic) ---
Public Class BomTreeBuilder
    Private Class BomProgressTracker
        Public TotalParts As Integer = 0
        Public CompletedParts As Integer = 0
        Public PdfsFound As Integer = 0
        Public MissingCount As Integer = 0
        Public Callback As Action(Of SearchProgressInfo)
        Public CancelToken As CancellationToken

        Public Sub CheckCancel()
            CancelToken.ThrowIfCancellationRequested()
        End Sub

        Public Sub Emit(Optional title As String = Nothing,
                        Optional detail As String = Nothing,
                        Optional logLine As String = Nothing,
                        Optional isIndeterminate As Nullable(Of Boolean) = Nothing)
            If Callback Is Nothing Then Return
            Callback.Invoke(New SearchProgressInfo With {
                .Title = title,
                .Detail = detail,
                .LogLine = logLine,
                .CompletedParts = CompletedParts,
                .TotalParts = TotalParts,
                .PdfsFound = PdfsFound,
                .MissingCount = MissingCount,
                .IsIndeterminate = isIndeterminate
            })
        End Sub
    End Class

    Public Shared Function IsGeneralArrangement(fileName As String) As Boolean
        If String.IsNullOrEmpty(fileName) Then Return False
        Dim pPartNum = Path.GetFileNameWithoutExtension(fileName)
        If pPartNum.EndsWith(".idw", StringComparison.OrdinalIgnoreCase) Then pPartNum = Path.GetFileNameWithoutExtension(pPartNum)
        Dim sIdx = pPartNum.IndexOf(" "c)
        If sIdx > 0 Then pPartNum = pPartNum.Substring(0, sIdx)
        Return pPartNum.EndsWith("-1000", StringComparison.OrdinalIgnoreCase)
    End Function

    Public Shared Function BuildTree(backend As IVaultBackend,
                                     rootFile As VaultFileInfo,
                                     list As List(Of PrintItem),
                                     ids As HashSet(Of Long),
                                     Optional progress As Action(Of SearchProgressInfo) = Nothing,
                                     Optional cancelToken As CancellationToken = Nothing) As PrintItem
        Dim tracker As New BomProgressTracker() With {
            .Callback = progress,
            .CancelToken = cancelToken
        }
        tracker.CheckCancel()

        Dim debugLog = Path.Combine(Path.GetTempPath(), "VaultPrintDebug_v3.log")
        Try : File.WriteAllText(debugLog, "--- START PDF-DRIVEN EXTRACT (" & backend.ModeName & ") ---" & vbCrLf) : Catch : End Try

        Dim rootItemName = If(rootFile.Name, "")
        Dim rootItemId = rootFile.Id

        Dim rootPdf As VaultFileInfo = Nothing
        Dim baseName = Path.GetFileNameWithoutExtension(rootItemName)
        If baseName.EndsWith(".idw", StringComparison.OrdinalIgnoreCase) Then baseName = Path.GetFileNameWithoutExtension(baseName)

        If rootItemName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) Then
            rootPdf = rootFile
        Else
            Dim pdfs = GetRootPdfs(backend, rootItemId, baseName, tracker)
            If pdfs.Count > 0 Then
                rootPdf = pdfs(0)
            End If
        End If

        If rootPdf Is Nothing OrElse rootPdf.Id = 0 Then
            Try : File.AppendAllText(debugLog, "No ROOT PDF found for " & rootItemName & vbCrLf) : Catch : End Try
            Return Nothing
        End If

        tracker.CheckCancel()
        tracker.Emit(detail:="Fetching metadata for " & rootPdf.Name & "...")
        Dim det = backend.GetFileDetails(rootPdf.Id)
        If det IsNot Nothing Then rootPdf = det
        tracker.CheckCancel()

        Dim rootIsRel As Boolean = rootPdf.IsReleased
        Dim rootRev As String = rootPdf.Revision
        If String.IsNullOrEmpty(rootRev) AndAlso rootFile IsNot Nothing Then
            rootRev = rootFile.Revision
        End If

        Dim rootDefaultChecked As Boolean = rootIsRel AndAlso Not IsGeneralArrangement(rootPdf.Name)
        Dim isRootDup As Boolean = ids.Contains(rootPdf.Id)
        Dim rootPdfItem = New PrintItem With {
            .Name = rootPdf.Name,
            .FileId = rootPdf.Id,
            .IsPDF = True,
            .IsDuplicate = isRootDup,
            .IsReleased = rootIsRel,
            .Revision = rootRev,
            .DefaultChecked = rootDefaultChecked,
            .IsChecked = (rootDefaultChecked AndAlso Not isRootDup),
            .IsExpanded = True
        }

        If Not rootPdfItem.IsDuplicate Then ids.Add(rootPdf.Id)
        list.Add(rootPdfItem)

        tracker.PdfsFound += 1
        Dim rootStatusLabel = If(isRootDup, "Duplicate", If(rootIsRel, "Released", "WIP"))
        Dim rootRevSuffix = If(String.IsNullOrEmpty(rootRev), "", " Rev " & rootRev)
        tracker.Emit(title:="Building BOM: " & rootPdf.Name,
                     detail:="Preparing BOM extraction for " & rootPdf.Name & "...",
                     logLine:=String.Format("Root drawing: {0} ({1}{2})", rootPdf.Name, rootStatusLabel, rootRevSuffix))

        Dim context As New HashSet(Of Long)()
        context.Add(rootPdf.Id)
        ProcessPdfRecursive(backend, rootPdf.Id, rootPdf.Name, rootPdfItem, list, ids, context, tracker, 0)

        Return rootPdfItem
    End Function

    Private Shared Function GetRootPdfs(backend As IVaultBackend, compId As Long, baseName As String, tracker As BomProgressTracker) As List(Of VaultFileInfo)
        Dim res As New List(Of VaultFileInfo)

        ' 1. Direct attachments
        tracker.CheckCancel()
        tracker.Emit(detail:="Checking direct Vault attachments...")
        Dim directAttach = backend.GetDirectAttachments(compId)
        If directAttach IsNot Nothing Then
            For Each d In directAttach
                If d IsNot Nothing AndAlso Not String.IsNullOrEmpty(d.Name) AndAlso d.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) Then
                    res.Add(d)
                End If
            Next
        End If
        If res.Count > 0 Then Return res

        ' 2. Parent associations (drawings)
        tracker.CheckCancel()
        tracker.Emit(detail:="Checking parent drawing associations...")
        Dim parents = backend.GetParentDependencies(compId)
        If parents IsNot Nothing Then
            For Each p In parents
                tracker.CheckCancel()
                If p IsNot Nothing AndAlso Not String.IsNullOrEmpty(p.Name) AndAlso
                   (p.Name.EndsWith(".idw", StringComparison.OrdinalIgnoreCase) OrElse p.Name.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase)) Then
                    Dim attach = backend.GetDirectAttachments(p.Id)
                    If attach IsNot Nothing Then
                        For Each a In attach
                            If a IsNot Nothing AndAlso Not String.IsNullOrEmpty(a.Name) AndAlso a.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) Then
                                res.Add(a)
                            End If
                        Next
                    End If
                End If
            Next
        End If
        If res.Count > 0 Then Return res

        ' 3. Name-based search
        tracker.CheckCancel()
        tracker.Emit(detail:="Searching Vault for matching root PDF...")
        Dim fallbacks = backend.SearchFilesByName(baseName & ".pdf", False)
        tracker.CheckCancel()
        If (fallbacks Is Nothing OrElse fallbacks.Count = 0) AndAlso Not String.IsNullOrEmpty(baseName) Then
            fallbacks = backend.SearchFilesByName(baseName & ".idw.pdf", False)
            tracker.CheckCancel()
        End If
        If fallbacks IsNot Nothing Then
            For Each f In fallbacks
                If f IsNot Nothing AndAlso Not String.IsNullOrEmpty(f.Name) AndAlso f.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) Then
                    res.Add(f)
                End If
            Next
        End If

        Return res
    End Function

    Private Shared Sub ProcessPdfRecursive(backend As IVaultBackend,
                                            pdfId As Long,
                                            pdfName As String,
                                            parentNode As PrintItem,
                                            list As List(Of PrintItem),
                                            ids As HashSet(Of Long),
                                            context As HashSet(Of Long),
                                            tracker As BomProgressTracker,
                                            depth As Integer)
        tracker.CheckCancel()
        Dim indentPrefix As String = If(depth > 0, New String(" "c, depth * 2) & "↳ ", "")

        Dim debugLog = Path.Combine(Path.GetTempPath(), "VaultPrintDebug_v3.log")
        Try : File.AppendAllText(debugLog, vbCrLf & "--- PROCESS PDF: " & pdfName & " ---" & vbCrLf) : Catch : End Try

        tracker.Emit(detail:="Downloading " & pdfName & " to read Parts List...",
                     logLine:=indentPrefix & "Downloading " & pdfName & "...")

        Dim tempPdfDir = Path.Combine(Path.GetTempPath(), "VaultPrintTool_v3_PDF")
        Dim tempPath = backend.DownloadFile(pdfId, pdfName, tempPdfDir, True)
        If String.IsNullOrEmpty(tempPath) OrElse Not File.Exists(tempPath) Then
            Try : File.AppendAllText(debugLog, "  FAILED to download " & pdfName & vbCrLf) : Catch : End Try
            tracker.Emit(logLine:=indentPrefix & "WARNING: Failed to download " & pdfName)
            Return
        End If

        Dim partNumbers As List(Of String)
        Try
            tracker.CheckCancel()
            tracker.Emit(detail:="Reading Parts List from " & pdfName & "...")
            partNumbers = ExtractPartNumbersFromPdf(tempPath)
        Finally
            Try : File.Delete(tempPath) : Catch : End Try
        End Try
        tracker.CheckCancel()

        Try : File.AppendAllText(debugLog, "  Extracted " & partNumbers.Count & " unique parts." & vbCrLf) : Catch : End Try

        tracker.TotalParts += partNumbers.Count
        tracker.Emit(detail:=String.Format("Extracted {0} unique parts from {1}.", partNumbers.Count, pdfName),
                     logLine:=indentPrefix & String.Format("Extracted {0} parts from {1}.", partNumbers.Count, pdfName))

        For Each pn In partNumbers
            tracker.CheckCancel()
            Try : File.AppendAllText(debugLog, "  - Checking Part: " & pn & vbCrLf) : Catch : End Try

            Dim nextIdx As Integer = tracker.CompletedParts + 1
            tracker.Emit(detail:=String.Format("Searching Vault for part {0} ({1} of {2})...", pn, nextIdx, tracker.TotalParts))

            Dim searchRes = backend.SearchFilesByName(pn, False)
            tracker.CheckCancel()

            If searchRes Is Nothing OrElse searchRes.Count = 0 Then
                tracker.CompletedParts += 1
                tracker.Emit(detail:=String.Format("Checked {0} ({1}/{2})", pn, tracker.CompletedParts, tracker.TotalParts),
                             logLine:=indentPrefix & String.Format("[{0}/{1}] {2} → Not in Vault (skipped)", tracker.CompletedParts, tracker.TotalParts, pn))
                Continue For
            End If

            Dim hasAssembly As Boolean = False
            Dim hasIpt As Boolean = False
            Dim foundPdfs As New List(Of VaultFileInfo)

            For Each f In searchRes
                If f Is Nothing OrElse String.IsNullOrEmpty(f.Name) Then Continue For

                Dim fBase = Path.GetFileNameWithoutExtension(f.Name)
                If fBase.EndsWith(".idw", StringComparison.OrdinalIgnoreCase) Then fBase = Path.GetFileNameWithoutExtension(fBase)

                Dim fPartNum = fBase
                Dim spIdx = fBase.IndexOf(" "c)
                If spIdx > 0 Then fPartNum = fBase.Substring(0, spIdx)

                Dim isExactMatch As Boolean = False
                If String.Equals(fPartNum, pn, StringComparison.OrdinalIgnoreCase) Then
                    isExactMatch = True
                Else
                    Dim underIdx = fPartNum.LastIndexOf("_"c)
                    If underIdx > 0 AndAlso String.Equals(fPartNum.Substring(0, underIdx), pn, StringComparison.OrdinalIgnoreCase) Then
                        isExactMatch = True
                    End If
                End If

                If isExactMatch AndAlso f.Name.EndsWith(".iam", StringComparison.OrdinalIgnoreCase) Then
                    hasAssembly = True
                ElseIf isExactMatch AndAlso f.Name.EndsWith(".ipt", StringComparison.OrdinalIgnoreCase) Then
                    hasIpt = True
                End If

                If Not isExactMatch Then Continue For

                If f.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) Then
                    foundPdfs.Add(f)
                End If
            Next

            If foundPdfs.Count = 0 Then
                Try : File.AppendAllText(debugLog, "    No Vault PDF matches found for " & pn & vbCrLf) : Catch : End Try

                Dim missingItem = New PrintItem With {
                    .Name = pn & ".idw.pdf",
                    .FileId = 0,
                    .IsPDF = True,
                    .IsDuplicate = False,
                    .IsMissing = True,
                    .IsExpanded = False,
                    .IsChecked = False
                }
                parentNode.Children.Add(missingItem)
                list.Add(missingItem)

                tracker.CompletedParts += 1
                tracker.MissingCount += 1
                tracker.Emit(detail:=String.Format("Missing PDF for {0} ({1}/{2})", pn, tracker.CompletedParts, tracker.TotalParts),
                             logLine:=indentPrefix & String.Format("[{0}/{1}] {2} → Missing PDF", tracker.CompletedParts, tracker.TotalParts, pn))
                Continue For
            End If

            Dim mainIndex As Integer = 0
            For i As Integer = 0 To foundPdfs.Count - 1
                Dim bName = Path.GetFileNameWithoutExtension(foundPdfs(i).Name)
                If bName.EndsWith(".idw", StringComparison.OrdinalIgnoreCase) Then bName = Path.GetFileNameWithoutExtension(bName)
                Dim sIdx = bName.IndexOf(" "c)
                Dim pNum = If(sIdx > 0, bName.Substring(0, sIdx), bName)
                If Not pNum.Contains("_") Then
                    mainIndex = i
                    Exit For
                End If
            Next

            Dim mainPdf = foundPdfs(mainIndex)
            Dim mainDet = backend.GetFileDetails(mainPdf.Id)
            If mainDet IsNot Nothing Then mainPdf = mainDet
            tracker.CheckCancel()

            Dim isMainDup = ids.Contains(mainPdf.Id)
            If Not isMainDup Then ids.Add(mainPdf.Id)

            Dim mainDefaultChecked As Boolean = mainPdf.IsReleased AndAlso Not IsGeneralArrangement(mainPdf.Name)
            Dim mainPdfItem = New PrintItem With {
                .Name = mainPdf.Name,
                .FileId = mainPdf.Id,
                .IsPDF = True,
                .IsDuplicate = isMainDup,
                .IsReleased = mainPdf.IsReleased,
                .Revision = mainPdf.Revision,
                .DefaultChecked = mainDefaultChecked,
                .IsChecked = (mainDefaultChecked AndAlso Not isMainDup),
                .IsExpanded = True
            }

            parentNode.Children.Add(mainPdfItem)
            list.Add(mainPdfItem)

            For i As Integer = 0 To foundPdfs.Count - 1
                If i = mainIndex Then Continue For
                Dim secPdf = foundPdfs(i)
                Dim secDet = backend.GetFileDetails(secPdf.Id)
                If secDet IsNot Nothing Then secPdf = secDet
                tracker.CheckCancel()

                Dim isSecDup = ids.Contains(secPdf.Id)
                If Not isSecDup Then ids.Add(secPdf.Id)

                Dim secDefaultChecked As Boolean = secPdf.IsReleased AndAlso Not IsGeneralArrangement(secPdf.Name)
                Dim secPdfItem = New PrintItem With {
                    .Name = secPdf.Name,
                    .FileId = secPdf.Id,
                    .IsPDF = True,
                    .IsDuplicate = isSecDup,
                    .IsReleased = secPdf.IsReleased,
                    .Revision = secPdf.Revision,
                    .DefaultChecked = secDefaultChecked,
                    .IsChecked = (secDefaultChecked AndAlso Not isSecDup),
                    .IsExpanded = True
                }

                mainPdfItem.Children.Add(secPdfItem)
                list.Add(secPdfItem)
            Next

            Dim shouldRecurse As Boolean = hasAssembly OrElse (Not hasIpt)
            Dim willRecurse As Boolean = shouldRecurse AndAlso Not isMainDup AndAlso Not context.Contains(mainPdf.Id)
            Dim isCycle As Boolean = shouldRecurse AndAlso Not isMainDup AndAlso context.Contains(mainPdf.Id)

            tracker.CompletedParts += 1
            tracker.PdfsFound += foundPdfs.Count

            Dim statusDesc As String = If(isMainDup, "Duplicate", If(mainPdf.IsReleased, "Released", "WIP"))
            If Not String.IsNullOrEmpty(mainPdf.Revision) Then statusDesc &= " Rev " & mainPdf.Revision
            Dim extraSheets As Integer = foundPdfs.Count - 1
            Dim extraSuffix As String = If(extraSheets > 0, String.Format(" (+{0} sheet/file)", extraSheets), "")
            Dim recurseSuffix As String = If(willRecurse, " [expanding sub-assembly]", "")

            tracker.Emit(detail:=String.Format("Resolved {0} ({1}/{2})", mainPdf.Name, tracker.CompletedParts, tracker.TotalParts),
                         logLine:=indentPrefix & String.Format("[{0}/{1}] {2} → {3} ({4}){5}{6}", tracker.CompletedParts, tracker.TotalParts, pn, mainPdf.Name, statusDesc, extraSuffix, recurseSuffix))

            If willRecurse Then
                Dim newContext As New HashSet(Of Long)(context)
                newContext.Add(mainPdf.Id)
                ProcessPdfRecursive(backend, mainPdf.Id, mainPdf.Name, mainPdfItem, list, ids, newContext, tracker, depth + 1)
            ElseIf isCycle Then
                Try : File.AppendAllText(debugLog, "    Skipping recursion for " & mainPdf.Name & " (Cycle detected)" & vbCrLf) : Catch : End Try
                tracker.Emit(logLine:=indentPrefix & "  Skipping " & mainPdf.Name & " (cycle detected)")
            End If
        Next
    End Sub

    Public Shared Function ExtractPartNumbersFromPdf(pdfPath As String) As List(Of String)
        Dim partNumbers As New List(Of String)
        Try
            Dim reader As New iTextSharp.text.pdf.PdfReader(pdfPath)
            Dim fullText As New System.Text.StringBuilder()

            For i As Integer = 1 To reader.NumberOfPages
                Dim text = iTextSharp.text.pdf.parser.PdfTextExtractor.GetTextFromPage(reader, i)
                fullText.AppendLine(text)
            Next
            reader.Close()

            Dim allText = fullText.ToString()

            Dim bomRegex As New System.Text.RegularExpressions.Regex("(?:\r?\n|^)[ \t]*(?:[A-Za-z]\s+)?(\d{1,3}(?:\.\d{1,3})*)\s+(?:\d+(?:\.\d+)*\s+)?([A-Z0-9][A-Z0-9\-_./]{4,25})\b")
            Dim matches = bomRegex.Matches(allText)
            For Each m As System.Text.RegularExpressions.Match In matches
                Dim pn = m.Groups(2).Value.Trim()
                If pn = "NOTE" OrElse pn = "FIT" OrElse pn = "THIRD" OrElse (pn.Contains("/") AndAlso pn.Length < 10) Then Continue For

                pn = System.Text.RegularExpressions.Regex.Replace(pn, "-MATL316", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                pn = System.Text.RegularExpressions.Regex.Replace(pn, "-MATL304", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase)

                If Not partNumbers.Contains(pn) Then
                    partNumbers.Add(pn)
                End If
            Next
        Catch ex As Exception
            Dim debugLog = Path.Combine(Path.GetTempPath(), "VaultPrintDebug_v3.log")
            Try : File.AppendAllText(debugLog, "PDF PARSE ERROR: " & ex.ToString() & vbCrLf) : Catch : End Try
        End Try
        Return partNumbers
    End Function
End Class

Public Class PrintManager
    Public Shared Sub PrintPDF(path As String, printerName As String)
        Try
            Using document As PdfDocument = PdfDocument.Load(path)
                Using printDoc As New PrintDocument()
                    printDoc.PrinterSettings.PrinterName = printerName
                    printDoc.DocumentName = System.IO.Path.GetFileName(path)

                    If Not printDoc.PrinterSettings.IsValid Then
                        MessageBox.Show("Printer '" & printerName & "' is not valid or accessible.", "Print Error", MessageBoxButton.OK, MessageBoxImage.Error)
                        Return
                    End If

                    Using printForm = document.CreatePrintDocument()
                        printForm.PrinterSettings = printDoc.PrinterSettings
                        printForm.PrintController = New StandardPrintController()
                        printForm.Print()
                    End Using
                End Using
            End Using
        Catch ex As Exception
            MessageBox.Show("Failed to print PDF: " & ex.Message, "Print Error", MessageBoxButton.OK, MessageBoxImage.Error)
        End Try
    End Sub
End Class
