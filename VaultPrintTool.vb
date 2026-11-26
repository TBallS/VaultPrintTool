Imports System
Imports System.Collections.Generic
Imports System.Collections.ObjectModel
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Reflection
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Media
Imports System.Windows.Markup
Imports System.Xml
Imports Autodesk.Connectivity.WebServices
Imports Autodesk.DataManagement.Client.Framework.Vault.Currency.Connections
Imports PdfiumViewer
Imports System.Drawing.Printing

<Assembly: AssemblyTitle("Vault Print Tool")>
<Assembly: AssemblyProduct("Vault Print Tool")>
<Assembly: AssemblyVersion("2.3.0.0")>
<Assembly: AssemblyFileVersion("2.3.0.0")>

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

' Main Logic
Public Class MainWindow
    Inherits Window

    ' --- Controls ---
    Private WithEvents btnLogin As Button
    Private WithEvents btnSearch As Button
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
    Private pnlGuide As Border
    Private pnlEmptyQueueHint As Border
    Private chkIncludeStp As CheckBox
    Private chkIncludeZip As CheckBox
    Private chkIncludeDxf As CheckBox
    Private chkGroupSubfolders As CheckBox
    Private pnlSearchBoxes As StackPanel
    Private WithEvents treeFiles As TreeView
    Private lstFiles As ListBox
    Private txtSearch As TextBox
    Private lblStatus As TextBlock
    Private txtServer As TextBox
    Private txtUser As TextBox
    Private txtPass As PasswordBox
    Private loginOverlay As Grid

    ' --- Validation ---
    Private vaultConn As Object = Nothing ' Connection object
    
    ' --- Data ---
    Private _flatList As New List(Of PrintItem)
    Private _processedIds As New HashSet(Of Long)
    Private _hideUnticked As Boolean = True

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
            </Style.Triggers>
        </Style>
        <Style TargetType="TextBox">
            <Setter Property="Background" Value="#252526"/>
            <Setter Property="Foreground" Value="White"/>
            <Setter Property="BorderBrush" Value="#3F3F46"/>
            <Setter Property="Padding" Value="5"/>
        </Style>
         <Style TargetType="PasswordBox">
            <Setter Property="Background" Value="#252526"/>
            <Setter Property="Foreground" Value="White"/>
            <Setter Property="BorderBrush" Value="#3F3F46"/>
            <Setter Property="Padding" Value="5"/>
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
                    <TextBlock Text="v2.3" Foreground="#AAA" FontSize="11" FontWeight="SemiBold"/>
                </Border>
            </StackPanel>
            <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center">
                 <Button x:Name="btnToggleGuide" Content="📖 User Guide" Background="#333" Foreground="White" BorderThickness="1" BorderBrush="#555" Margin="0,0,15,0" Padding="10,5" ToolTip="Toggle Step-by-Step Instructions &amp; Drawing Status Guide"/>
                 <TextBlock x:Name="lblUserInfo" Text="Not Connected" Foreground="#AAA" VerticalAlignment="Center" Margin="0,0,10,0"/>
                 <Button x:Name="btnLogout" Content="Log Out" Visibility="Collapsed" Background="#C44"/>
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
                            <TextBox x:Name="txtSearch" Margin="0,0,5,0" ToolTip="Enter drawing or assembly part number (e.g. 22000-1080)"/>
                            <Button x:Name="btnSearch" Grid.Column="1" Content="Go" Background="#007ACC" ToolTip="Search Vault and automatically parse drawing BOM"/>
                            <!-- A Remove placeholder to keep spacing aligned if we add remove buttons to others -->
                            <Button Content="X" Grid.Column="2" Visibility="Hidden" Margin="5,0,0,0" Width="20" />
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
                    <TreeView.ItemTemplate>
                        <HierarchicalDataTemplate ItemsSource="{Binding Children}">
                            <StackPanel Orientation="Horizontal" Margin="2">
                                <CheckBox IsChecked="{Binding IsChecked}" VerticalAlignment="Center" IsEnabled="{Binding IsEnabled}"/>
                                <TextBlock Text="{Binding Name}" Margin="5,0" VerticalAlignment="Center">
                                    <TextBlock.Style>
                                        <Style TargetType="TextBlock">
                                            <Setter Property="Foreground" Value="White" />
                                            <Style.Triggers>
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
                    <ListBox.ItemContainerStyle>
                        <Style TargetType="ListBoxItem">
                            <Setter Property="Visibility" Value="{Binding UIVisibility}"/>
                        </Style>
                    </ListBox.ItemContainerStyle>
                    <ListBox.ItemTemplate>
                        <DataTemplate>
                            <StackPanel Orientation="Horizontal" Margin="2,4">
                                <CheckBox IsChecked="{Binding IsChecked}" VerticalAlignment="Center" IsEnabled="{Binding IsEnabled}" Margin="0,0,10,0"/>
                                <TextBlock Text="{Binding Name}" VerticalAlignment="Center" TextWrapping="Wrap" MaxWidth="500">
                                    <TextBlock.Style>
                                        <Style TargetType="TextBlock">
                                            <Setter Property="Foreground" Value="White" />
                                            <Style.Triggers>
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
                                <TextBlock Text="Type the top-level part or GA number (e.g. 22000-1080) and click 'Go'. If a selection list appears, pick the top-level *.idw drawing (highlighted as ★ RECOMMENDED) to extract the entire assembly drawing package. Pick individual parts/models if you only need that specific item." Foreground="#AAA" FontSize="10" TextWrapping="Wrap" Margin="0,0,0,4" LineHeight="14"/>

                                <TextBlock Text="2. Pure PDF BOM Extraction" FontWeight="SemiBold" Foreground="#EEE" FontSize="11" Margin="0,1,0,1"/>
                                <TextBlock Text="The tool automatically reads the drawing's Parts List table from Vault, recursing through all sub-assemblies (.iam) and components." Foreground="#AAA" FontSize="10" TextWrapping="Wrap" Margin="0,0,0,4" LineHeight="14"/>

                                <TextBlock Text="3. Review Queue &amp; Export" FontWeight="SemiBold" Foreground="#EEE" FontSize="11" Margin="0,1,0,1"/>
                                <TextBlock Text="Check or uncheck drawings as needed. Click 'Export to ZIP' to package production drawings, or 'Print Selected' to print directly." Foreground="#AAA" FontSize="10" TextWrapping="Wrap" LineHeight="14"/>
                            </StackPanel>
                        </Border>

                        <!-- Queue Views & Filter Controls Card (User Request) -->
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
            
            <StatusBar Background="Transparent" Foreground="#AAA">
                <TextBlock x:Name="lblStatus" Text="Ready"/>
            </StatusBar>
            
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
        <Border Background="#1E1E1E" Width="350" Padding="20" VerticalAlignment="Center" CornerRadius="8" BorderBrush="#007ACC" BorderThickness="1">
            <StackPanel>
                <TextBlock Text="Vault Login" FontSize="20" Foreground="White" Margin="0,0,0,20" HorizontalAlignment="Center"/>
                
                <TextBlock Text="Server" Foreground="#AAA"/>
                <TextBox x:Name="txtServer" Text="" Margin="0,5,0,10"/>
                
                <TextBlock Text="User" Foreground="#AAA"/>
                <TextBox x:Name="txtUser" Text="" Margin="0,5,0,10"/>
                
                <TextBlock Text="Password" Foreground="#AAA"/>
                <PasswordBox x:Name="txtPass" Margin="0,5,0,20"/>
                
                <Button x:Name="btnLogin" Content="Connect" Background="#007ACC" Height="35"/>
            </StackPanel>
        </Border>
    </Grid>

</Grid>

        ' --- 2. Load XAML ---
        Me.Title = "Vault Print Tool v2.3"
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
        btnAddSearch = root.FindName("btnAddSearch")
        btnClearQueue = root.FindName("btnClearQueue")
        chkIncludeStp = root.FindName("chkIncludeStp")
        chkIncludeZip = root.FindName("chkIncludeZip")
        chkIncludeDxf = root.FindName("chkIncludeDxf")
        chkGroupSubfolders = root.FindName("chkGroupSubfolders")
        pnlSearchBoxes = root.FindName("pnlSearchBoxes")
        btnExportZip = root.FindName("btnExportZip")
        btnExportText = root.FindName("btnExportText")
        btnExportMissing = root.FindName("btnExportMissing")
        btnPrint = root.FindName("btnPrint")
        treeFiles = root.FindName("treeFiles")
        lstFiles = root.FindName("lstFiles")
        btnToggleView = root.FindName("btnToggleView")
        btnToggleFilter = root.FindName("btnToggleFilter")
        lblStatus = root.FindName("lblStatus")
        
        btnToggleGuide = root.FindName("btnToggleGuide")
        btnCloseGuide = root.FindName("btnCloseGuide")
        chkShowGuideStartup = root.FindName("chkShowGuideStartup")
        pnlGuide = root.FindName("pnlGuide")
        pnlEmptyQueueHint = root.FindName("pnlEmptyQueueHint")

        loginOverlay = root.FindName("loginOverlay")
        txtServer = root.FindName("txtServer")
        txtUser = root.FindName("txtUser")
        txtPass = root.FindName("txtPass")
        btnLogin = root.FindName("btnLogin")

        ' Initialize Guide Visibility from user preference
        Dim guidePref As Boolean = GetGuidePreference()
        SetGuideVisible(guidePref)
        If chkShowGuideStartup IsNot Nothing Then
            chkShowGuideStartup.IsChecked = guidePref
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
                btnToggleGuide.Background = New SolidColorBrush(Color.FromRgb(0, 122, 204)) ' #007ACC active
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
        Return True ' Default to True so new users receive immediate guidance
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

    ' --- Event Handlers ---
    Private Sub BtnLogin_Click(sender As Object, e As RoutedEventArgs) Handles btnLogin.Click
         Dim srv = txtServer.Text.Trim()
         Dim usr = txtUser.Text.Trim()
         Dim pwd = txtPass.Password
         If String.IsNullOrWhiteSpace(srv) Then
             MessageBox.Show("Please enter a Vault server address.")
             Return
         End If
         
         lblStatus.Text = "Connecting..."
         Application.Current.Dispatcher.Invoke(Sub() 
             Try
                 vaultConn = VaultService.Login(srv, "Vault", usr, pwd)
                 If vaultConn IsNot Nothing Then
                     loginOverlay.Visibility = Visibility.Collapsed
                     lblStatus.Text = "Connected."
                     CType(Me.Content, FrameworkElement).FindName("lblUserInfo").Text = "User: " & usr
                 End If
             Catch ex As Exception
                 MessageBox.Show("Login Failed: " & ex.Message)
                 lblStatus.Text = "Login Failed."
             End Try
         End Sub, System.Windows.Threading.DispatcherPriority.Background)
    End Sub

    Private Sub BtnAddSearch_Click(sender As Object, e As RoutedEventArgs) Handles btnAddSearch.Click
        Dim grid As New Grid()
        grid.Margin = New Thickness(0, 0, 0, 5)
        
        Dim col1 As New ColumnDefinition() With { .Width = New GridLength(1, GridUnitType.Star) }
        Dim col2 As New ColumnDefinition() With { .Width = GridLength.Auto }
        Dim col3 As New ColumnDefinition() With { .Width = GridLength.Auto }
        grid.ColumnDefinitions.Add(col1)
        grid.ColumnDefinitions.Add(col2)
        grid.ColumnDefinitions.Add(col3)
        
        Dim txt As New TextBox()
        txt.Margin = New Thickness(0, 0, 5, 0)
        
        Dim tbStyle As New Style(GetType(TextBox))
        tbStyle.Setters.Add(New Setter(TextBox.BackgroundProperty, New SolidColorBrush(Color.FromRgb(&H25, &H25, &H26))))
        tbStyle.Setters.Add(New Setter(TextBox.ForegroundProperty, Brushes.White))
        tbStyle.Setters.Add(New Setter(TextBox.BorderBrushProperty, New SolidColorBrush(Color.FromRgb(&H3F, &H3F, &H46))))
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
        
        Grid.SetColumn(btn, 1)
        AddHandler btn.Click, AddressOf BtnSearch_Click
        grid.Children.Add(btn)
        
        Dim btnRemove As New Button()
        btnRemove.Content = "X"
        btnRemove.Background = New SolidColorBrush(Color.FromRgb(&HC4, &H44, &H44))
        btnRemove.Foreground = Brushes.White
        btnRemove.Margin = New Thickness(5, 0, 0, 0)
        btnRemove.Width = 20
        btnRemove.Padding = New Thickness(2, 5, 2, 5)
        btnRemove.BorderThickness = New Thickness(0)
        
        Grid.SetColumn(btnRemove, 2)
        AddHandler btnRemove.Click, Sub(s, ev)
                                        pnlSearchBoxes.Children.Remove(grid)
                                    End Sub
        grid.Children.Add(btnRemove)
        
        pnlSearchBoxes.Children.Add(grid)
    End Sub

    Private Sub BtnClearQueue_Click(sender As Object, e As RoutedEventArgs) Handles btnClearQueue.Click
        _flatList.Clear()
        _processedIds.Clear()
        treeFiles.ItemsSource = Nothing
        If lstFiles IsNot Nothing AndAlso lstFiles.Visibility = Visibility.Visible Then
            lstFiles.ItemsSource = Nothing
        End If
        If pnlEmptyQueueHint IsNot Nothing Then
            pnlEmptyQueueHint.Visibility = Visibility.Visible
        End If
        lblStatus.Text = "Queue cleared."
        ApplyFilter()
    End Sub

    Private Sub BtnSearch_Click(sender As Object, e As RoutedEventArgs) Handles btnSearch.Click
        If vaultConn Is Nothing Then Return
        
        Dim term As String = ""
        If TypeOf sender Is Button Then
             Dim btn As Button = DirectCast(sender, Button)
             Dim parent As Grid = TryCast(btn.Parent, Grid)
             If parent IsNot Nothing Then
                 For Each child In parent.Children
                     If TypeOf child Is TextBox Then
                         term = DirectCast(child, TextBox).Text.Trim()
                         Exit For
                     End If
                 Next
             End If
        End If
        
        If String.IsNullOrEmpty(term) Then term = txtSearch.Text.Trim()
        If String.IsNullOrEmpty(term) Then Return
        
        lblStatus.Text = "Searching Vault for " & term & "..."
        
        Application.Current.Dispatcher.Invoke(Sub() 
             Try
                 ' 1. Search for all matches
                 Dim rawMatches = VaultService.SearchForFiles(vaultConn, term)
                 
                 If rawMatches Is Nothing OrElse rawMatches.Count = 0 Then
                     lblStatus.Text = "No file found for: " & term
                     Return
                 End If
                 
                 ' 2. Filter and Sort
                 Dim validMatches As New List(Of Object)
                 Dim primaryMatches As New List(Of Object)
                 Dim seenIds As New HashSet(Of Long)()
                 
                 For Each f In rawMatches
                     Dim fId As Long = 0
                     Try : fId = CLng(VaultService.GetProp(f, "Id")) : Catch : End Try
                     If fId > 0 AndAlso seenIds.Contains(fId) Then Continue For
                     If fId > 0 Then seenIds.Add(fId)

                     Dim n As String = "Unknown"
                     Dim val = VaultService.GetProp(f, "Name")
                     If val Is Nothing Then val = VaultService.GetProp(f, "EntityName")
                     If val IsNot Nothing Then n = val.ToString()
                     
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
                         if isPrimary Then primaryMatches.Add(f)
                     End If
                 Next
                 
                 ' If we have IDWs/IAMs, prioritize them completely
                 If primaryMatches.Count > 0 Then
                     validMatches = primaryMatches
                 End If
                 
                 ' If filtering left us with nothing, go back to all raw matches (minus Zips if possible)
                 If validMatches.Count = 0 Then
                     For Each f In rawMatches
                         Dim n As String = ""
                         Dim val = VaultService.GetProp(f, "Name")
                         If val IsNot Nothing Then n = val.ToString()
                         If Not n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) Then validMatches.Add(f)
                     Next
                 End If
                 
                 ' Final Fallback
                 If validMatches.Count = 0 Then validMatches.AddRange(rawMatches)
                 
                 ' Sort
                 validMatches.Sort(New NameSorter(term))

                 ' 3. Select
                 Dim selectedFile As Object = Nothing
                 If validMatches.Count = 1 Then
                     selectedFile = validMatches(0)
                 Else
                     ' Show Dialog with explicit count
                     Dim selDlg As New SelectorForm(validMatches, term)
                     If selDlg.ShowDialog() = True Then
                         selectedFile = selDlg.SelectedFile
                     Else
                         lblStatus.Text = "Ready."
                         Return
                     End If
                 End If
                 
                 If selectedFile Is Nothing Then Return

                 ' 4. Build Tree
                 Dim rootItem As PrintItem = VaultService.BuildTree(vaultConn, selectedFile, _flatList, _processedIds)
                 If rootItem IsNot Nothing Then
                     Dim list As ObservableCollection(Of PrintItem) = TryCast(treeFiles.ItemsSource, ObservableCollection(Of PrintItem))
                     If list Is Nothing Then
                         list = New ObservableCollection(Of PrintItem)()
                         treeFiles.ItemsSource = list
                     End If
                     list.Add(rootItem)
                     If pnlEmptyQueueHint IsNot Nothing Then
                         pnlEmptyQueueHint.Visibility = Visibility.Collapsed
                     End If
                     lblStatus.Text = "Found " & rootItem.Name & ". Expanded references."
                     btnPrint.IsEnabled = True
                     btnExportZip.IsEnabled = True
                     
                     ApplyFilter()
                     
                     If lstFiles IsNot Nothing AndAlso lstFiles.Visibility = Visibility.Visible Then
                         RefreshListView()
                     End If
                 Else
                     lblStatus.Text = "Could not build tree for selection."
                 End If

                 
             Catch ex As Exception
                 MessageBox.Show("Search Error: " & ex.Message & vbCrLf & ex.StackTrace)
             End Try
        End Sub, System.Windows.Threading.DispatcherPriority.Background)
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
        Implements IComparer(Of Object)
        
        Private _term As String
        Public Sub New(term As String)
            _term = term
        End Sub
        
        Public Function Compare(x As Object, y As Object) As Integer Implements IComparer(Of Object).Compare
            Dim nA As String = "Unknown"
            Dim nB As String = "Unknown"
            Try : nA = CStr(x.GetType().GetProperty("Name").GetValue(x, Nothing)) : Catch : End Try
            Try : nB = CStr(y.GetType().GetProperty("Name").GetValue(y, Nothing)) : Catch : End Try
            
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

    Private Sub DownloadItemAndAttachments(vaultConn As Object, item As PrintItem, destDir As String, ByRef count As Integer, includeStp As Boolean, includeZip As Boolean, includeDxf As Boolean)
        lblStatus.Text = String.Format("Downloading: {0}", item.Name)
        Application.Current.Dispatcher.Invoke(Sub() lblStatus.UpdateLayout(), System.Windows.Threading.DispatcherPriority.Background) 
        
        Dim localPath = VaultService.DownloadFile(vaultConn, item.FileId, destDir)
        If System.IO.File.Exists(localPath) Then count += 1

        If includeStp OrElse includeZip OrElse includeDxf Then
            Dim rawName = item.Name
            If rawName.EndsWith(".idw.pdf", StringComparison.OrdinalIgnoreCase) Then rawName = rawName.Substring(0, rawName.Length - 8)
            If rawName.EndsWith(".ipt.pdf", StringComparison.OrdinalIgnoreCase) Then rawName = rawName.Substring(0, rawName.Length - 8)
            If rawName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) Then rawName = rawName.Substring(0, rawName.Length - 4)

            lblStatus.Text = "Searching for related files for " & rawName & "..."
            Application.Current.Dispatcher.Invoke(Sub() lblStatus.UpdateLayout(), System.Windows.Threading.DispatcherPriority.Background)
            
            Dim relatedFiles = VaultService.SearchForFiles(vaultConn, rawName)
            If relatedFiles IsNot Nothing Then
                For Each relFile In relatedFiles
                    Dim relName As String = CStr(VaultService.GetProp(relFile, "Name"))
                    If relName Is Nothing Then Continue For
                    
                    Dim isMatch = False
                    If includeStp AndAlso (relName.Equals(rawName & ".idw.stp", StringComparison.OrdinalIgnoreCase) OrElse relName.Equals(rawName & ".ipt.stp", StringComparison.OrdinalIgnoreCase) OrElse relName.Equals(rawName & ".stp", StringComparison.OrdinalIgnoreCase)) Then isMatch = True
                    If includeZip AndAlso (relName.Equals(rawName & ".idw.zip", StringComparison.OrdinalIgnoreCase) OrElse relName.Equals(rawName & ".ipt.zip", StringComparison.OrdinalIgnoreCase) OrElse relName.Equals(rawName & ".zip", StringComparison.OrdinalIgnoreCase)) Then isMatch = True
                    If includeDxf AndAlso (relName.Equals(rawName & ".idw.dxf", StringComparison.OrdinalIgnoreCase) OrElse relName.Equals(rawName & ".ipt.dxf", StringComparison.OrdinalIgnoreCase) OrElse relName.Equals(rawName & ".dxf", StringComparison.OrdinalIgnoreCase)) Then isMatch = True
                    
                    If isMatch Then
                        Dim mId As Long = CLng(VaultService.GetProp(relFile, "Id"))
                        If mId > 0 Then
                            lblStatus.Text = "Downloading " & relName & "..."
                            Application.Current.Dispatcher.Invoke(Sub() lblStatus.UpdateLayout(), System.Windows.Threading.DispatcherPriority.Background)
                            Dim matchLocalPath = VaultService.DownloadFile(vaultConn, mId, destDir)
                            If System.IO.File.Exists(matchLocalPath) Then count += 1
                        End If
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

    Private Sub BtnExportZip_Click(sender As Object, e As RoutedEventArgs) Handles btnExportZip.Click
        Dim toExport = _flatList.Where(Function(p) p.IsChecked AndAlso p.IsPDF).ToList()
        
        If toExport.Count = 0 Then
            MessageBox.Show("No PDFs selected to export.")
            Return
        End If
        
        Dim rootName = _flatList(0).Name
        Dim baseName = Path.GetFileNameWithoutExtension(rootName)
        If baseName.EndsWith(".idw", StringComparison.OrdinalIgnoreCase) Then baseName = Path.GetFileNameWithoutExtension(baseName)
        If baseName.EndsWith(".iam", StringComparison.OrdinalIgnoreCase) Then baseName = Path.GetFileNameWithoutExtension(baseName)
        If baseName.EndsWith(".ipt", StringComparison.OrdinalIgnoreCase) Then baseName = Path.GetFileNameWithoutExtension(baseName)
        
        ' Retrieve revision letter from root item or Vault
        Dim rev As String = ""
        If _flatList.Count > 0 Then
            rev = _flatList(0).Revision
            If String.IsNullOrWhiteSpace(rev) AndAlso vaultConn IsNot Nothing AndAlso _flatList(0).FileId > 0 Then
                Dim dummyRel As Boolean = False
                VaultService.GetFileDetails(vaultConn, _flatList(0).FileId, dummyRel, rev)
                _flatList(0).Revision = rev
            End If
        End If

        Dim zipFileName As String
        If Not String.IsNullOrEmpty(rev) Then
            If baseName.IndexOf("Rev", StringComparison.OrdinalIgnoreCase) >= 0 Then
                zipFileName = baseName & ".zip"
            Else
                zipFileName = String.Format("{0} Rev {1}.zip", baseName, rev)
            End If
        Else
            zipFileName = baseName & ".zip"
        End If
        
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
                                  
                                  DownloadItemAndAttachments(vaultConn, item, destDir, count, includeStp, includeZip, includeDxf)
                             Next
                         Next
                     Else
                         For Each item In toExport
                              DownloadItemAndAttachments(vaultConn, item, tempDir, count, includeStp, includeZip, includeDxf)
                         Next
                     End If
                     
                     lblStatus.Text = "Zipping files..."
                     Application.Current.Dispatcher.Invoke(Sub() lblStatus.UpdateLayout(), System.Windows.Threading.DispatcherPriority.Background)
                     
                     ' Exclude any _V cache folders and 1kb metadata files
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
        ' Collect checked items from flat list (maintained during recursion)
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

                     ' 1. Download
                     Dim localPath = VaultService.DownloadFile(vaultConn, item.FileId, tempDir)
                     If System.IO.File.Exists(localPath) Then
                         ' 2. Print
                         PrintManager.PrintPDF(localPath, queue.FullName)
                         count += 1
                     End If
                Next
                lblStatus.Text = String.Format("Sent {0} jobs to printer.", count)
                MessageBox.Show(Me, "Printing Complete.")
            Catch ex As Exception
                MessageBox.Show(Me, "Print Error: " & ex.Message)
            Finally
                ' Cleanup (Optional, maybe keep for debug?)
                ' Directory.Delete(tempDir, True)
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
        If items(0).Children.Count > 0 Then
             Dim firstBaseName = System.IO.Path.GetFileNameWithoutExtension(items(0).Children(0).Name)
             If firstBaseName.EndsWith(".idw", StringComparison.OrdinalIgnoreCase) Then firstBaseName = System.IO.Path.GetFileNameWithoutExtension(firstBaseName)
             dName = firstBaseName & "_List"
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
        If _flatList.Count > 0 Then
             Dim firstBaseName = System.IO.Path.GetFileNameWithoutExtension(_flatList(0).Name)
             If firstBaseName.EndsWith(".idw", StringComparison.OrdinalIgnoreCase) Then firstBaseName = System.IO.Path.GetFileNameWithoutExtension(firstBaseName)
             dName = firstBaseName & "_Missing"
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

        ' Escape quotes and commas
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
        If treeFiles.ItemsSource Is Nothing Then Return
        
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
        
        ' Update Button States
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
            ' Switch to List
            treeFiles.Visibility = Visibility.Collapsed
            lstFiles.Visibility = Visibility.Visible
            btnToggleView.Content = "🌲 Switch to Tree View"
            btnToggleView.ToolTip = "Switch back to hierarchical assembly tree view (shows parent & sub-assemblies)"
            RefreshListView()
        Else
            ' Switch to Tree
            lstFiles.Visibility = Visibility.Collapsed
            treeFiles.Visibility = Visibility.Visible
            btnToggleView.Content = "📋 Switch to List View"
            btnToggleView.ToolTip = "Switch to flat list view (shows all drawings in a single sorted list)"
        End If
    End Sub

End Class



' --- Helper Dialog (Distinct Class) ---
Public Class SelectorForm
    Inherits Window
    
    Public Property SelectedFile As Object
    Private _lst As ListBox
    Private _map As New Dictionary(Of Integer, Object)

    Public Sub New(files As List(Of Object), Optional searchTerm As String = "")
        Me.Title = "Select File (" & files.Count & " matches)"
        Me.Width = 680
        Me.Height = 520
        Me.WindowStartupLocation = WindowStartupLocation.CenterScreen
        Me.Topmost = True
        Me.Background = New SolidColorBrush(Color.FromRgb(30, 30, 30))
        
        Dim grid As New Grid()
        grid.RowDefinitions.Add(New RowDefinition() With { .Height = GridLength.Auto })
        grid.RowDefinitions.Add(New RowDefinition() With { .Height = New GridLength(1, GridUnitType.Star) })
        grid.RowDefinitions.Add(New RowDefinition() With { .Height = GridLength.Auto })
        
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
        pnlBottom.ColumnDefinitions.Add(New ColumnDefinition() With { .Width = New GridLength(1, GridUnitType.Star) })
        pnlBottom.ColumnDefinitions.Add(New ColumnDefinition() With { .Width = GridLength.Auto })

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
        _btnOk.Background = New SolidColorBrush(Color.FromRgb(0, 122, 204)) ' #007ACC
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
            Dim name As String = "Unknown Item"
            Dim val = VaultService.GetProp(f, "Name")
            If val Is Nothing Then val = VaultService.GetProp(f, "EntityName")
            If val IsNot Nothing Then
                name = val.ToString()
            ElseIf f IsNot Nothing Then
                name = "Object Type: " & f.GetType().Name
            End If
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
                Dim topRow As New StackPanel() With { .Orientation = Orientation.Horizontal }
                
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
                
                ' Add subtitle for clarity
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
    Public Property IsDuplicate As Boolean
    Public Property IsReleased As Boolean
    Public Property Revision As String = ""
    
    Public Property IsMissing As Boolean
    
    ' UI
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

' --- Services ---
Public Class VaultService
    ' --- Reflection Helpers ---
    Public Shared Function GetProp(obj As Object, prop As String) As Object
        If obj Is Nothing Then Return Nothing
        Dim p = obj.GetType().GetProperty(prop)
        If p IsNot Nothing Then Return p.GetValue(obj, Nothing)
        Return Nothing
    End Function

    Private Shared Function Invoke(obj As Object, method As String, params As Object()) As Object
        If obj Is Nothing Then Return Nothing
        Dim m = obj.GetType().GetMethod(method)
        If m IsNot Nothing Then Return m.Invoke(obj, params)
        Return Nothing
    End Function

    ' --- Login ---
    Public Shared Function Login(server As String, vault As String, user As String, pass As String) As Object
        Dim path2025 = "C:\Program Files\Autodesk\Vault Client 2025\Explorer"
        If Not Directory.Exists(path2025) Then 
             Throw New Exception("Vault Client 2025 path not found: " & path2025)
        End If

        ' Load Helper: Load standard + Tools
        Dim dlls = {"Autodesk.Connectivity.WebServices.dll", 
                    "Autodesk.DataManagement.Client.Framework.Vault.dll", 
                    "Autodesk.DataManagement.Client.Framework.dll",
                    "Autodesk.Connectivity.WebServices.Tools.dll"} ' Added Tools DLL
                    
        For Each d In dlls
             Try : Assembly.LoadFrom(Path.Combine(path2025, d)) : Catch : End Try
        Next

        ' Helper to find Type by Name (robust)
        Dim FindType = Function(name As String) As Type
                           ' 1. Fast Lookup
                           For Each a In AppDomain.CurrentDomain.GetAssemblies()
                               Dim t = a.GetType("Autodesk.Connectivity.WebServicesTools." & name)
                               If t Is Nothing Then t = a.GetType("Autodesk.Connectivity.WebServices.Tools." & name)
                               If t IsNot Nothing Then Return t
                           Next
                           
                           ' 2. Slow Scan (Fallback)
                           For Each a In AppDomain.CurrentDomain.GetAssemblies()
                               If Not a.FullName.StartsWith("Autodesk") Then Continue For
                               Try
                                   For Each t In a.GetTypes()
                                       If t.Name = name Then Return t
                                   Next
                               Catch : End Try
                           Next
                           Return Nothing
                       End Function

        ' 1. ServerIdentities
        Dim siType = FindType("ServerIdentities")
        If siType Is Nothing Then Throw New Exception("Could not find ServerIdentities type. Loaded Assemblies: " & AppDomain.CurrentDomain.GetAssemblies().Count())

        Dim si = Activator.CreateInstance(siType)
        siType.GetProperty("DataServer").SetValue(si, server, Nothing)
        siType.GetProperty("FileServer").SetValue(si, server, Nothing)

        ' 2. Credentials
        Dim credType = FindType("UserPasswordCredentials")
        If credType Is Nothing Then Throw New Exception("Could not find UserPasswordCredentials type.")
        
        ' Constructor might vary (AuthFlags vs Boolean)
        ' Try finding constructor with 5 args, last one Boolean OR Enum
        Dim creds As Object = Nothing
        For Each c In credType.GetConstructors()
            Dim ps = c.GetParameters()
            If ps.Length = 5 Then
                ' Try invoking with ReadOnly=True (Boolean)
                Try
                     creds = c.Invoke(New Object() {si, vault, user, pass, True})
                     Exit For
                Catch
                End Try
                ' Try invoking with LicensingAgent=None (0) (Enum/Int)
                Try
                     creds = c.Invoke(New Object() {si, vault, user, pass, 0})
                     Exit For
                Catch
                End Try
            End If
        Next
        
        If creds Is Nothing Then Throw New Exception("Could not create Credentials. No matching constructor found.")

        ' 3. WebServiceManager
        Dim wsmType = FindType("WebServiceManager")
        If wsmType Is Nothing Then Throw New Exception("Could not find WebServiceManager type.")
        
        Dim wsm = Activator.CreateInstance(wsmType, New Object() {creds})

        ' 4. VDF Connection
         Dim connType As Type = Nothing
         ' Robust Lookup for Connection type
         For Each a In AppDomain.CurrentDomain.GetAssemblies()
             If a.FullName.Contains("Autodesk.DataManagement.Client.Framework.Vault") Then
                 connType = a.GetType("Autodesk.DataManagement.Client.Framework.Vault.Currency.Connections.Connection")
                 If connType IsNot Nothing Then Exit For
             End If
         Next
         
         If connType Is Nothing Then Throw New Exception("Could not find VDF Connection type.")
         
         If connType Is Nothing Then Throw New Exception("Could not find VDF Connection type.")
         
         ' Constructor Loop (Ported from VaultAttachmentCollector)
         ' VDF Connection constructors vary by version. We need to find one that accepts WebServiceManager.
         Dim vdfConn As Object = Nothing
         
         For Each c As ConstructorInfo In connType.GetConstructors()
             Dim ps = c.GetParameters()
             ' Key check: First param must be WebServiceManager
             If ps.Length >= 1 AndAlso ps(0).ParameterType.Name.Contains("WebServiceManager") Then
                 Dim cArgs(ps.Length - 1) As Object
                 cArgs(0) = wsm
                 
                 ' Intelligently fill other args
                 For i As Integer = 1 To ps.Length - 1
                     Dim pType As Type = ps(i).ParameterType
                     
                     If pType Is GetType(String) Then
                         cArgs(i) = String.Empty
                         ' Try to map specific strings based on typical order
                         If ps.Length = 5 Then
                             ' 5 params: wsMgr, User, UserID, VaultName, Flags
                             If i = 1 Then cArgs(i) = user
                             If i = 3 Then cArgs(i) = vault
                         ElseIf ps.Length = 7 Then
                              ' 7 params: wsMgr, User, Pass, Server, UserID, VaultName, Flags
                              If i = 1 Then cArgs(i) = user
                              If i = 2 Then cArgs(i) = pass
                              If i = 3 Then cArgs(i) = vault 
                              If i = 5 Then cArgs(i) = vault 
                         End If
                     ElseIf pType Is GetType(Long) OrElse pType Is GetType(Int64) Then
                         cArgs(i) = 0L ' UserID 0
                     ElseIf pType.IsEnum OrElse pType Is GetType(Integer) Then
                         cArgs(i) = 0
                     End If
                 Next
                 
                 Try
                     vdfConn = c.Invoke(cArgs)
                     Exit For
                 Catch : End Try
             End If
         Next
         
         If vdfConn Is Nothing Then Throw New Exception("Could not find suitable VDF Connection constructor.")
         
         Return vdfConn
    End Function

    ' --- Search & Tree ---
    Public Shared Function SearchForFiles(conn As Object, term As String) As List(Of Object)
        Dim results As New List(Of Object)
        
        Dim files = FindFileByName(conn, term)
        If files IsNot Nothing Then
             Dim arr As Array = DirectCast(files, Array)
             For Each f As Object In arr
                 results.Add(f)
             Next
        End If
        
        Return results
    End Function

    Public Shared Function SearchForFilesExact(conn As Object, term As String) As List(Of Object)
        Dim results As New List(Of Object)
        
        Dim files = FindFileByName(conn, term, True)
        If files IsNot Nothing Then
             Dim arr As Array = DirectCast(files, Array)
             For Each f As Object In arr
                 results.Add(f)
             Next
        End If
        
        Return results
    End Function

    Public Shared Function BuildTree(conn As Object, rootFile As Object, list As List(Of PrintItem), ids As HashSet(Of Long)) As PrintItem
        Dim debugLog = Path.Combine(Path.GetTempPath(), "VaultPrintDebug.log")
        System.IO.File.WriteAllText(debugLog, "--- START PDF-DRIVEN EXTRACT ---" & vbCrLf)
        
        Dim rootItemName = CStr(GetProp(rootFile, "Name"))
        Dim rootItemId = CLng(GetProp(rootFile, "Id"))
        
        Dim rootPdfId As Long = 0
        Dim rootPdfName As String = ""
        Dim baseName = Path.GetFileNameWithoutExtension(rootItemName)
        If baseName.EndsWith(".idw", StringComparison.OrdinalIgnoreCase) Then baseName = Path.GetFileNameWithoutExtension(baseName)
        
        If rootItemName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) Then
            rootPdfId = rootItemId
            rootPdfName = rootItemName
        Else
            Dim pdfs = GetRootPdfs(conn, rootItemId, baseName)
            If pdfs.Count > 0 Then
                rootPdfId = pdfs(0).Item1
                rootPdfName = pdfs(0).Item2
            End If
        End If
        
        If rootPdfId = 0 Then
            System.IO.File.AppendAllText(debugLog, "No ROOT PDF found for " & rootItemName & vbCrLf)
            Return Nothing
        End If
        
        Dim rootIsRel As Boolean = False
        Dim rootRev As String = ""
        GetFileDetails(conn, rootPdfId, rootIsRel, rootRev)
        If String.IsNullOrEmpty(rootRev) AndAlso rootFile IsNot Nothing Then
            Dim frev = GetProp(rootFile, "FileRev")
            If frev IsNot Nothing Then
                Dim lbl = CStr(GetProp(frev, "Label"))
                If Not String.IsNullOrEmpty(lbl) Then rootRev = lbl.Trim()
            End If
        End If

        Dim rootPdfItem = New PrintItem With {
            .Name = rootPdfName, .FileId = rootPdfId, .IsPDF = True, .IsDuplicate = ids.Contains(rootPdfId),
            .IsReleased = rootIsRel, .Revision = rootRev, .IsExpanded = True
        }
        
        If rootPdfItem.IsReleased AndAlso Not rootPdfItem.IsDuplicate Then
            Dim pPartNum = Path.GetFileNameWithoutExtension(rootPdfName)
            If pPartNum.EndsWith(".idw", StringComparison.OrdinalIgnoreCase) Then pPartNum = Path.GetFileNameWithoutExtension(pPartNum)
            Dim sIdx = pPartNum.IndexOf(" "c)
            If sIdx > 0 Then pPartNum = pPartNum.Substring(0, sIdx)
            
            If pPartNum.EndsWith("-1000", StringComparison.OrdinalIgnoreCase) Then
                rootPdfItem.IsChecked = False
            Else
                rootPdfItem.IsChecked = True
            End If
        End If
        
        If Not rootPdfItem.IsDuplicate Then ids.Add(rootPdfId)
        list.Add(rootPdfItem)
        
        Dim context As New HashSet(Of Long)()
        context.Add(rootPdfId)
        ProcessPdfRecursive(conn, rootPdfId, rootPdfName, rootPdfItem, list, ids, context)
        
        Return rootPdfItem
    End Function

    Private Shared Function GetRootPdfs(conn As Object, compId As Long, baseName As String) As List(Of Tuple(Of Long, String))
        Dim res As New List(Of Tuple(Of Long, String))
        
        ' 1. Direct attachments
        Dim directAttach = GetAttachments(conn, compId)
        If directAttach IsNot Nothing Then
            For Each d In directAttach
                Dim dId As Long = GetProp(d, "CldFileId")
                Dim dName As String = GetFileName(conn, dId)
                If dName IsNot Nothing AndAlso dName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) Then
                    res.Add(Tuple.Create(dId, dName))
                End If
            Next
        End If
        If res.Count > 0 Then Return res
        
        ' 2. Parent associations (drawings)
        Dim parents = GetAssociations(conn, compId, False)
        If parents IsNot Nothing Then
            For Each p In parents
                Dim pId As Long = GetProp(p, "ParFileId")
                Dim pName As String = GetFileName(conn, pId)
                If pName IsNot Nothing AndAlso (pName.EndsWith(".idw", StringComparison.OrdinalIgnoreCase) OrElse pName.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase)) Then
                    Dim attach = GetAttachments(conn, pId)
                    If attach IsNot Nothing Then
                        For Each a In attach
                            Dim aId As Long = GetProp(a, "CldFileId")
                            Dim aName As String = GetFileName(conn, aId)
                            If aName IsNot Nothing AndAlso aName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) Then
                                res.Add(Tuple.Create(aId, aName))
                            End If
                        Next
                    End If
                End If
            Next
        End If
        If res.Count > 0 Then Return res
        
        ' 3. Name-based search
        Dim fallbacks = FindFileByName(conn, baseName & ".pdf", False)
        If fallbacks IsNot Nothing Then
            Dim arr As Array = DirectCast(fallbacks, Array)
            For Each f As Object In arr
                Dim fId As Long = GetProp(f, "Id")
                Dim fName As String = GetProp(f, "Name")
                If fName IsNot Nothing AndAlso fName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) Then
                    res.Add(Tuple.Create(fId, fName))
                End If
            Next
        End If
        
        Return res
    End Function

    Private Shared Sub ProcessPdfRecursive(conn As Object, pdfId As Long, pdfName As String, parentNode As PrintItem, list As List(Of PrintItem), ids As HashSet(Of Long), context As HashSet(Of Long))
        Dim debugLog = Path.Combine(Path.GetTempPath(), "VaultPrintDebug.log")
        System.IO.File.AppendAllText(debugLog, vbCrLf & "--- PROCESS PDF: " & pdfName & " ---" & vbCrLf)
        
        Dim tempPath = DownloadFileToTemp(conn, pdfId, pdfName)
        If tempPath Is Nothing Then
            System.IO.File.AppendAllText(debugLog, "  FAILED to download " & pdfName & vbCrLf)
            Return
        End If
        
        Dim partNumbers = ExtractPartNumbersFromPdf(tempPath)
        Try : System.IO.File.Delete(tempPath) : Catch : End Try
        
        System.IO.File.AppendAllText(debugLog, "  Extracted " & partNumbers.Count & " unique parts." & vbCrLf)
        
        For Each pn In partNumbers
            System.IO.File.AppendAllText(debugLog, "  - Checking Part: " & pn & vbCrLf)
            
            Dim searchRes = FindFileByName(conn, pn, False)
            If searchRes Is Nothing Then Continue For
            
            Dim arr As Array = DirectCast(searchRes, Array)
            
            Dim hasAssembly As Boolean = False
            Dim hasIpt As Boolean = False
            Dim foundPdfIds As New List(Of Tuple(Of Long, String))
            
            For Each f As Object In arr
                Dim fId As Long = GetProp(f, "Id")
                Dim fName As String = CStr(GetProp(f, "Name"))
                If fName Is Nothing Then Continue For
                
                Dim fBase = Path.GetFileNameWithoutExtension(fName)
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
                
                If isExactMatch AndAlso fName.EndsWith(".iam", StringComparison.OrdinalIgnoreCase) Then
                    hasAssembly = True
                ElseIf isExactMatch AndAlso fName.EndsWith(".ipt", StringComparison.OrdinalIgnoreCase) Then
                    hasIpt = True
                End If
                
                If Not isExactMatch Then
                    Continue For
                End If
                
                If fName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) Then
                    foundPdfIds.Add(Tuple.Create(fId, fName))
                End If
            Next
            
            If foundPdfIds.Count = 0 Then
                System.IO.File.AppendAllText(debugLog, "    No Vault PDF matches found for " & pn & vbCrLf)
                
                Dim missingItem = New PrintItem With {
                    .Name = pn & ".idw.pdf", .FileId = 0, .IsPDF = True, .IsDuplicate = False,
                    .IsMissing = True, .IsExpanded = False, .IsChecked = False
                }
                parentNode.Children.Add(missingItem)
                list.Add(missingItem)
                
                Continue For
            End If
            
            Dim mainIndex As Integer = 0
            For i As Integer = 0 To foundPdfIds.Count - 1
                 Dim bName = Path.GetFileNameWithoutExtension(foundPdfIds(i).Item2)
                 If bName.EndsWith(".idw", StringComparison.OrdinalIgnoreCase) Then bName = Path.GetFileNameWithoutExtension(bName)
                 Dim sIdx = bName.IndexOf(" "c)
                 Dim pNum = If(sIdx > 0, bName.Substring(0, sIdx), bName)
                 If Not pNum.Contains("_") Then
                     mainIndex = i
                     Exit For
                 End If
            Next
            
            Dim mainPdf = foundPdfIds(mainIndex)
            Dim isMainDup = ids.Contains(mainPdf.Item1)
            If Not isMainDup Then ids.Add(mainPdf.Item1)
            
            Dim mainIsRel As Boolean = False
            Dim mainRev As String = ""
            GetFileDetails(conn, mainPdf.Item1, mainIsRel, mainRev)

            Dim mainPdfItem = New PrintItem With {
                .Name = mainPdf.Item2, .FileId = mainPdf.Item1, .IsPDF = True, .IsDuplicate = isMainDup,
                .IsReleased = mainIsRel, .Revision = mainRev, .IsExpanded = True
            }
            
            If mainPdfItem.IsReleased AndAlso Not mainPdfItem.IsDuplicate Then
                Dim pPartNum = Path.GetFileNameWithoutExtension(mainPdf.Item2)
                If pPartNum.EndsWith(".idw", StringComparison.OrdinalIgnoreCase) Then pPartNum = Path.GetFileNameWithoutExtension(pPartNum)
                Dim sIdx = pPartNum.IndexOf(" "c)
                If sIdx > 0 Then pPartNum = pPartNum.Substring(0, sIdx)
                
                If pPartNum.EndsWith("-1000", StringComparison.OrdinalIgnoreCase) Then
                    mainPdfItem.IsChecked = False
                Else
                    mainPdfItem.IsChecked = True
                End If
            End If
            
            parentNode.Children.Add(mainPdfItem)
            list.Add(mainPdfItem)
            
            For i As Integer = 0 To foundPdfIds.Count - 1
                If i = mainIndex Then Continue For
                Dim secPdf = foundPdfIds(i)
                Dim isSecDup = ids.Contains(secPdf.Item1)
                If Not isSecDup Then ids.Add(secPdf.Item1)
                
                Dim secIsRel As Boolean = False
                Dim secRev As String = ""
                GetFileDetails(conn, secPdf.Item1, secIsRel, secRev)

                Dim secPdfItem = New PrintItem With {
                    .Name = secPdf.Item2, .FileId = secPdf.Item1, .IsPDF = True, .IsDuplicate = isSecDup,
                    .IsReleased = secIsRel, .Revision = secRev, .IsExpanded = True
                }
                
                If secPdfItem.IsReleased AndAlso Not secPdfItem.IsDuplicate Then
                    Dim pPartNum = Path.GetFileNameWithoutExtension(secPdf.Item2)
                    If pPartNum.EndsWith(".idw", StringComparison.OrdinalIgnoreCase) Then pPartNum = Path.GetFileNameWithoutExtension(pPartNum)
                    Dim sIdx = pPartNum.IndexOf(" "c)
                    If sIdx > 0 Then pPartNum = pPartNum.Substring(0, sIdx)
                    
                    If pPartNum.EndsWith("-1000", StringComparison.OrdinalIgnoreCase) Then
                        secPdfItem.IsChecked = False
                    Else
                        secPdfItem.IsChecked = True
                    End If
                End If
                
                mainPdfItem.Children.Add(secPdfItem)
                list.Add(secPdfItem)
            Next
            
            Dim shouldRecurse As Boolean = hasAssembly OrElse (Not hasIpt)
            If shouldRecurse AndAlso Not isMainDup Then
                If Not context.Contains(mainPdf.Item1) Then
                    Dim newContext As New HashSet(Of Long)(context)
                    newContext.Add(mainPdf.Item1)
                    ProcessPdfRecursive(conn, mainPdf.Item1, mainPdf.Item2, mainPdfItem, list, ids, newContext)
                Else
                    System.IO.File.AppendAllText(debugLog, "    Skipping recursion for " & mainPdf.Item2 & " (Cycle detected)" & vbCrLf)
                End If
            End If
            
        Next
    End Sub

    Private Shared Function CreatePrintItem(conn As Object, fileObj As Object, ids As HashSet(Of Long)) As PrintItem
        Dim fid As Long = GetProp(fileObj, "Id")
        Dim fname As String = CStr(GetProp(fileObj, "Name"))
        If fname Is Nothing Then fname = "Unknown"
        Dim isDup = ids.Contains(fid)
        If Not isDup Then ids.Add(fid)
        
        Dim isRel As Boolean = False
        Dim rev As String = ""
        GetFileDetails(conn, fid, isRel, rev)
        If String.IsNullOrEmpty(rev) AndAlso fileObj IsNot Nothing Then
            Dim frev = GetProp(fileObj, "FileRev")
            If frev IsNot Nothing Then
                Dim lbl = CStr(GetProp(frev, "Label"))
                If Not String.IsNullOrEmpty(lbl) Then rev = lbl.Trim()
            End If
        End If

        Dim p = New PrintItem With {
            .Name = fname, .FileId = fid, .IsDuplicate = isDup, .IsPDF = fname.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase),
            .IsReleased = isRel, .Revision = rev
        }
        
        If p.IsPDF AndAlso Not p.IsDuplicate AndAlso p.IsReleased Then p.IsChecked = True
        Return p
    End Function

    Private Shared Function CheckIfReleased(conn As Object, fid As Long) As Boolean
        Dim isRel As Boolean = False
        Dim rev As String = ""
        GetFileDetails(conn, fid, isRel, rev)
        Return isRel
    End Function

    Public Shared Function GetFileDetails(conn As Object, fid As Long, ByRef isReleased As Boolean, ByRef revision As String) As Boolean
        isReleased = False
        revision = ""
        Try
            Dim wsm = GetProp(conn, "WebServiceManager")
            Dim docSvc = GetProp(wsm, "DocumentService")
            Dim res = Invoke(docSvc, "GetLatestFilesByIds", New Object() {New Long() {fid}})
            Dim arr As Array = DirectCast(res, Array)
            If arr IsNot Nothing AndAlso arr.Length > 0 AndAlso arr.GetValue(0) IsNot Nothing Then
                Dim f = arr.GetValue(0)
                Dim lfc = GetProp(f, "FileLfCyc")
                If lfc IsNot Nothing Then
                    Dim state = CStr(GetProp(lfc, "LfCycStateName"))
                    isReleased = String.Equals(state, "Released", StringComparison.OrdinalIgnoreCase)
                End If
                Dim frev = GetProp(f, "FileRev")
                If frev IsNot Nothing Then
                    Dim lbl = CStr(GetProp(frev, "Label"))
                    If Not String.IsNullOrEmpty(lbl) Then revision = lbl.Trim()
                End If
                Return True
            End If
        Catch
        End Try
        Return False 
    End Function

    Private Shared Function GetFileName(conn As Object, fileId As Long) As String
        Try
            Dim wsm = GetProp(conn, "WebServiceManager")
            Dim docSvc = GetProp(wsm, "DocumentService")
            Dim res = Invoke(docSvc, "GetLatestFilesByIds", New Object() {New Long() {fileId}})
            If res IsNot Nothing Then
                Dim arr As Array = DirectCast(res, Array)
                If arr.Length > 0 AndAlso arr.GetValue(0) IsNot Nothing Then
                    Return CStr(GetProp(arr.GetValue(0), "Name"))
                End If
            End If
        Catch
        End Try
        Return ""
    End Function


    Private Shared Function DownloadFileToTemp(conn As Object, fileId As Long, fileName As String) As String
        Dim debugLog = Path.Combine(Path.GetTempPath(), "VaultPrintDebug.log")
        Try
            Dim tempDir = Path.Combine(Path.GetTempPath(), "VaultPrintTool_PDF")
            If Not Directory.Exists(tempDir) Then Directory.CreateDirectory(tempDir)
            Dim tempPath = Path.Combine(tempDir, fileName)
            If System.IO.File.Exists(tempPath) Then System.IO.File.Delete(tempPath)
            
            Dim wsm = GetProp(conn, "WebServiceManager")
            Dim docSvc = GetProp(wsm, "DocumentService")
            
            Dim res = Invoke(docSvc, "GetLatestFilesByIds", New Object() {New Long() {fileId}})
            Dim arr As Array = DirectCast(res, Array)
            If arr Is Nothing OrElse arr.Length = 0 Then Return Nothing
            Dim fileObj = arr.GetValue(0)
            
            Dim acqMethod = docSvc.GetType().GetMethod("GetDownloadTicketsByFileIds")
            If acqMethod Is Nothing Then Return Nothing
            
            Dim tickets = acqMethod.Invoke(docSvc, New Object() {New Long() {fileId}})
            If tickets Is Nothing Then Return Nothing
            
            Dim ticketArr As Array = DirectCast(tickets, Array)
            If ticketArr.Length = 0 Then Return Nothing
            
            Dim ticket = ticketArr.GetValue(0)
            Dim ticketBytes As Byte() = DirectCast(GetProp(ticket, "Bytes"), Byte())
            
            Dim filestoreSvc = GetProp(wsm, "FilestoreService")
            Dim dlMethod = filestoreSvc.GetType().GetMethod("DownloadFilePart")
            If dlMethod Is Nothing Then Return Nothing
            
            Dim fileSize As Long = CLng(GetProp(fileObj, "FileSize"))
            If fileSize <= 0 Then fileSize = 50000000L
            
            Dim dlResult = dlMethod.Invoke(filestoreSvc, New Object() {ticketBytes, 0L, fileSize, True})
            If dlResult Is Nothing Then Return Nothing
            
            Using ms As New System.IO.MemoryStream()
                Dim stream As System.IO.Stream = DirectCast(dlResult, System.IO.Stream)
                stream.CopyTo(ms)
                Dim fileBytes = ms.ToArray()
                System.IO.File.WriteAllBytes(tempPath, fileBytes)
                System.IO.File.AppendAllText(debugLog, "DOWNLOAD: OK " & fileName & " (" & fileBytes.Length & " bytes)" & vbCrLf)
                stream.Close()
            End Using
            Return tempPath
        Catch ex As Exception
            System.IO.File.AppendAllText(debugLog, "DOWNLOAD ERROR: " & ex.ToString() & vbCrLf)
        End Try
        Return Nothing
    End Function
    
    Private Shared Function ExtractPartNumbersFromPdf(pdfPath As String) As List(Of String)
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
            
            If pdfPath.Contains("310") OrElse pdfPath.Contains("1080") OrElse pdfPath.Contains("1077") Then
                 Dim dumpLog = Path.Combine(Path.GetTempPath(), "VaultPrintPdfText.txt")
                 System.IO.File.AppendAllText(dumpLog, "--- DUMP OF " & Path.GetFileName(pdfPath) & " ---" & vbCrLf & allText & vbCrLf & "--- END ---" & vbCrLf)
            End If
            
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
            Dim debugLog = Path.Combine(Path.GetTempPath(), "VaultPrintDebug.log")
            System.IO.File.AppendAllText(debugLog, "PDF PARSE ERROR: " & ex.ToString() & vbCrLf)
        End Try
        Return partNumbers
    End Function

    ' --- Helpers ---
    Private Shared Function FindFileByName(conn As Object, name As String, Optional exactMatch As Boolean = False) As Object
        Dim wsm = GetProp(conn, "WebServiceManager")
        Dim docSvc = GetProp(wsm, "DocumentService")
        Dim propSvc = GetProp(wsm, "PropertyService")
        
        ' 1. Find PropDefId for "File Name" or "Name" dynamically
        Dim namePropId As Long = 15 ' Fallback
        Try
            Dim getPropDefs = propSvc.GetType().GetMethod("GetPropertyDefinitionsByEntityClassId")
            If getPropDefs IsNot Nothing Then
                Dim defs = getPropDefs.Invoke(propSvc, New Object() {"FILE"})
                Dim arr = DirectCast(defs, Array)
                For Each def As Object In arr
                    Dim sysName = CStr(GetProp(def, "SysName"))
                    Dim dispName = CStr(GetProp(def, "DispName"))
                    If sysName = "Name" OrElse dispName = "File Name" Then
                        namePropId = CLng(GetProp(def, "Id"))
                        Exit For
                    End If
                Next
            End If
        Catch : End Try

        ' 2. Find Method
        Dim findMethod As MethodInfo = Nothing
        For Each mi As MethodInfo In docSvc.GetType().GetMethods()
            If mi.Name = "FindFilesBySearchConditions" Then
                findMethod = mi
                Exit For
            End If
        Next
        If findMethod Is Nothing Then Return Nothing

        ' 3. Build Conditions
        Dim params As ParameterInfo() = findMethod.GetParameters()
        Dim srchCondType As Type = params(0).ParameterType.GetElementType()

        Dim cond As Object = Activator.CreateInstance(srchCondType)
        SetPropSafe(cond, "PropDefId", namePropId)
        
        If exactMatch Then
             SetPropSafe(cond, "SrchOper", 3)      ' 3 = Equals
        Else
             SetPropSafe(cond, "SrchOper", 1)      ' 1 = Contains
        End If
        
        SetPropSafe(cond, "SrchTxt", name)
        SetPropSafe(cond, "PropTyp", "SingleProperty")
        SetPropSafe(cond, "SrchRule", "Must")

        Dim conditions As Array = Array.CreateInstance(srchCondType, 1)
        conditions.SetValue(cond, 0)
        
        ' 4. Invoke
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
                args(i) = True ' Recurse
            Else
                Try : args(i) = Activator.CreateInstance(p.ParameterType) : Catch : args(i) = Nothing : End Try
            End If
        Next
        
        Dim limitBreak As Integer = 0
        Dim allResults As New System.Collections.ArrayList()
        
        Do
            Dim result = findMethod.Invoke(docSvc, args)
            If result IsNot Nothing Then
                Dim arr As Array = DirectCast(result, Array)
                For Each item As Object In arr
                    allResults.Add(item)
                Next
            End If
            
            Dim bookmark As String = ""
            If bookmarkIndex > 0 AndAlso args(bookmarkIndex) IsNot Nothing Then
                bookmark = CStr(args(bookmarkIndex))
            End If
            
            If String.IsNullOrEmpty(bookmark) Then Exit Do
            
            limitBreak += 1
            If limitBreak > 50 Then Exit Do ' Safe-guard against infinite loop
        Loop
        
        If allResults.Count > 0 Then
            Return allResults.ToArray()
        Else
            Return Nothing
        End If
    End Function
    
    Private Shared Sub SetPropSafe(obj As Object, prop As String, val As Object)
        Try
            Dim p = obj.GetType().GetProperty(prop)
            If p.PropertyType.IsEnum Then
                 p.SetValue(obj, [Enum].ToObject(p.PropertyType, val), Nothing)
            Else
                 p.SetValue(obj, val, Nothing)
            End If
        Catch : End Try
    End Sub



    Public Shared Function DownloadFile(conn As Object, id As Long, folder As String) As String
        ' Use AcquireFiles
        Dim fm = GetProp(conn, "FileManager")
        
        Dim FindType = Function(typeName As String) As Type
                           For Each a In AppDomain.CurrentDomain.GetAssemblies()
                               If a.FullName.Contains("Autodesk") Then
                                   Dim t = a.GetType(typeName)
                                   If t IsNot Nothing Then Return t
                               End If
                           Next
                           Return Nothing
                       End Function

        Dim fIterType = FindType("Autodesk.DataManagement.Client.Framework.Vault.Currency.Entities.FileIteration")
        Dim settingsType = FindType("Autodesk.DataManagement.Client.Framework.Vault.Settings.AcquireFilesSettings")
        Dim fpType = FindType("Autodesk.DataManagement.Client.Framework.Currency.FolderPathAbsolute")
        
        If fIterType Is Nothing Then Throw New Exception("VDF Types missing")

        ' Create FileIteration
        ' Constructor (Connection, File) -> Needs full File object. 
        ' We have ID. Get File Loop.
        Dim wsm = GetProp(conn, "WebServiceManager")
        Dim ds = GetProp(wsm, "DocumentService")
        Dim fArr = Invoke(ds, "GetLatestFilesByIds", New Object() {New Long() {id}})
        Dim fileObj = DirectCast(fArr, Array).GetValue(0)
        
        Dim fIter = Activator.CreateInstance(fIterType, New Object() {conn, fileObj})
        
        ' Settings
        Dim settings = Activator.CreateInstance(settingsType, New Object() {conn, False})
        
        ' Disable creation of _V metadata cache files
        Dim propMeta = settings.GetType().GetProperty("CreateMetaDataFile")
        If propMeta IsNot Nothing Then
            propMeta.SetValue(settings, False, Nothing)
        End If
        
        ' AddFileToAcquire(fIter, AcquireType.Download)
        Dim mAdd = settings.GetType().GetMethod("AddFileToAcquire", New Type() {fIterType, settings.GetType().Assembly.GetType("Autodesk.DataManagement.Client.Framework.Vault.Settings.AcquireFilesSettings+AcquisitionOption")})
        ' The Enum type is tricky to get by name if nested.
        ' Try iterating methods.
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
        
        ' Local Path
        ' settings.LocalPath = new FolderPathAbsolute(folder)
        Dim fp = Activator.CreateInstance(fpType, New Object() {folder})
        settings.GetType().GetProperty("LocalPath").SetValue(settings, fp, Nothing)
        
        ' Execute
        Dim res = Invoke(fm, "AcquireFiles", New Object() {settings})
        
        ' Return path
        Dim name = GetProp(fileObj, "Name")
        Return Path.Combine(folder, name)
    End Function

    Private Shared Function GetAssociations(conn As Object, id As Long, dependencies As Boolean) As IEnumerable(Of Object)
        Dim list As New List(Of Object)
        Dim wsm = GetProp(conn, "WebServiceManager")
        Dim docSvc = GetProp(wsm, "DocumentService")
        
        Dim method = docSvc.GetType().GetMethod("GetFileAssociationLitesByIds")
        Dim ps = method.GetParameters()
        Dim ids = {id}
        
        If ps.Length = 9 Then
            Dim alg = [Enum].ToObject(ps(1).ParameterType, 1) ' LatestTip
            Dim typeNone = [Enum].ToObject(ps(2).ParameterType, 0)
            Dim typeDep = [Enum].ToObject(ps(4).ParameterType, 3) ' Dependency
            
            Dim pType As Object = typeNone
            Dim childType As Object = typeNone
            
            If dependencies Then
                childType = typeDep
            Else
                pType = typeDep
            End If

            Dim args = New Object() {ids, alg, pType, False, childType, False, False, False, False}
            
            Dim res = method.Invoke(docSvc, args)
            If res IsNot Nothing Then
                Dim arr As Array = DirectCast(res, Array)
                For Each item In arr
                    list.Add(item)
                Next
            End If
        End If
        Return list
    End Function

    Private Shared Function GetAttachments(conn As Object, id As Long) As IEnumerable(Of Object)
        Dim list As New List(Of Object)
        Dim wsm = GetProp(conn, "WebServiceManager")
        Dim docSvc = GetProp(wsm, "DocumentService")
        Dim method = docSvc.GetType().GetMethod("GetFileAssociationLitesByIds")
        Dim ps = method.GetParameters()
        Dim ids = {id}
        If ps.Length = 9 Then
            Dim alg = [Enum].ToObject(ps(1).ParameterType, 1) ' LatestTip
            Dim typeNone = [Enum].ToObject(ps(2).ParameterType, 0)
            Dim typeAttach = [Enum].ToObject(ps(4).ParameterType, 1) ' Attachment
            Dim args = New Object() {ids, alg, typeNone, False, typeAttach, False, False, True, False}
            Dim res = method.Invoke(docSvc, args)
            If res IsNot Nothing Then
                Dim arr As Array = DirectCast(res, Array)
                For Each item In arr
                    list.Add(item)
                Next
            End If
        End If
        Return list
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

                    ' Connect the PdfDocument to the PrintDocument
                    Using printForm = document.CreatePrintDocument()
                        printForm.PrinterSettings = printDoc.PrinterSettings
                        printForm.PrintController = New StandardPrintController() ' Hides the "Printing page X of Y" dialog
                        printForm.Print()
                    End Using
                End Using
            End Using
        Catch ex As Exception
            MessageBox.Show("Failed to print PDF: " & ex.Message, "Print Error", MessageBoxButton.OK, MessageBoxImage.Error)
        End Try
    End Sub
End Class
