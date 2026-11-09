using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Presenters;
using Avalonia.Automation;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Kopt2026.App;
using Kopt2026.Application;
using Kopt2026.Domain;
using Kopt2026.Persistence;

namespace Kopt2026.App;

public sealed class MainWindow : Window
{
    internal DesktopWorkspace Workspace { get; }
    internal MainViewModel ViewModel => Workspace.ViewModel;
    internal TextBox ModelEditor { get; }
    internal TextBox ResultOutput { get; }
    internal Button SolveButton { get; }
    internal SolutionGraph Graph { get; }
    internal TabControl ResultsTabs { get; }
    internal DataGrid VariablesGrid { get; }
    internal DataGrid AlternativeSolutionsGrid { get; }
    internal ScrollViewer GraphScrollViewer { get; }
    internal Grid EditorOutputGrid { get; }
    internal MenuItem PrecisionMenu => precision;
    internal MenuItem KoreanTermsMenu => koreanTerms;
    internal MenuItem InterceptsMenu => intercepts;
    private readonly List<(DataGrid Grid, Column[] Columns)> resultGrids = [];
    private readonly List<(MenuItem Item, int Digits)> precisionItems = [];
    private readonly MenuItem koreanTerms;
    private readonly MenuItem intercepts;
    private readonly MenuItem precision;
    private readonly List<ICommand> fileCommands = [];
    private bool allowClose;
    private bool checkingClose;

    public MainWindow() : this(null) { }
    internal MainWindow(DesktopWorkspace? workspace)
    {
        var dialogs = new WorkspaceDialogs(this);
        Workspace = workspace ?? new DesktopWorkspace(dialogs);
        DataContext = ViewModel;
        Title = Workspace.Title;
        Width = 1240; Height = 800; MinWidth = 920; MinHeight = 640;
        FontSize = 13;
        FontFamily = new FontFamily("Segoe UI,Apple SD Gothic Neo,Malgun Gothic,sans-serif");
        ApplyWorkspaceStyles();
        Background = new SolidColorBrush(Color.Parse("#F1F5F9"));
        var root = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,Auto,3*,6,2*,Auto") };
        root.RowDefinitions[2].MinHeight = 285;
        root.RowDefinitions[4].MinHeight = 180;
        var newCommand = FileCommand(() => Workspace.NewAsync());
        var openCommand = FileCommand(() => Workspace.OpenAsync());
        var saveCommand = FileCommand(() => Workspace.SaveAsync());
        var saveAsCommand = FileCommand(() => Workspace.SaveAsync(true));
        var exportCommand = FileCommand(() => Workspace.ExportAsync());
        var fileMenu = new MenuItem
        {
            Header = "파일",
            ItemsSource = new object[]
            {
                Menu("새 문서", newCommand), Menu("열기…", openCommand), new Separator(),
                Menu("저장", saveCommand), Menu("다른 이름으로 저장…", saveAsCommand),
                new Separator(), Menu("결과 내보내기…", exportCommand), Menu("종료", new RelayCommand(Close))
            }
        };
        koreanTerms = new MenuItem { Header = "결과 표 항목을 한글로 표시", ToggleType = MenuItemToggleType.CheckBox, IsChecked = true };
        intercepts = new MenuItem { Header = "그래프에 X·Y절편 표시", ToggleType = MenuItemToggleType.CheckBox, IsChecked = true };
        koreanTerms.Click += async (_, _) => await SaveDisplaySettingsAsync();
        intercepts.Click += async (_, _) => await SaveDisplaySettingsAsync();
        precision = new MenuItem { Header = "소수 자릿수: 6자리" };
        for (var digits = ResultDisplayPrecision.Minimum; digits <= ResultDisplayPrecision.Maximum; digits++)
        {
            var selected = digits;
            var item = new MenuItem { Header = $"{digits}자리", ToggleType = MenuItemToggleType.CheckBox, IsChecked = digits == ViewModel.DecimalPlaces };
            item.Click += async (_, _) => await SaveDisplaySettingsAsync(selected);
            precisionItems.Add((item, digits));
        }
        precision.ItemsSource = precisionItems.Select(pair => pair.Item).ToArray();
        var settingsMenu = new MenuItem { Header = "설정", ItemsSource = new object[] { koreanTerms, intercepts, precision } };
        settingsMenu.Bind(IsEnabledProperty, new Binding(nameof(Workspace.SettingsReady)) { Source = Workspace });
        var menus = new Menu
        {
            ItemsSource = new object[]
            {
                fileMenu,
                new MenuItem { Header = "실행", ItemsSource = new object[]
                { Menu("문서 검증", ViewModel.ValidateCommand), Menu("최적화 실행", ViewModel.RunCommand), Menu("실행 취소", ViewModel.CancelCommand) } },
                settingsMenu,
                new MenuItem { Header = "도움말", ItemsSource = new[]
                { Menu("K-OPT 2026 정보", new AsyncRelayCommand(() => dialogs.MessageAsync("K-OPT 2026", "교육용 선형·정수 최적화 프로그램\n공통 앱 미리보기 0.3.4 · HiGHS 1.15.1"))) } }
            }
        };
        root.Children.Add(menus);
        SolveButton = Button("실행", ViewModel.RunCommand, "M4 2 L14 8 L4 14 Z", true);
        SolveButton.Classes.Add("primary");
        SolveButton.Background = new SolidColorBrush(Color.Parse("#2563EB"));
        SolveButton.Foreground = Brushes.White;
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(8, 5),
            Children =
            {
                Button("새 문서", newCommand, "M3 2 H9 L13 6 V14 H3 Z M9 2 V6 H13 M6 9 H10 M8 7 V11"),
                Button("열기", openCommand, "M2 6 V4 H6 L8 6 H14 V8 M2 7 H15 L12 14 H2 Z"),
                Button("저장", saveCommand, "M2 2 H12 L14 4 V14 H2 Z M5 2 V6 H11 V2 M5 14 V9 H11 V14"),
                new Border { Width = 1, Height = 18, Background = new SolidColorBrush(Color.Parse("#E2E8F0")), Margin = new Thickness(7, 0), VerticalAlignment = VerticalAlignment.Center },
                Button("검증", ViewModel.ValidateCommand, "M14 8 A6 6 0 1 1 8 2 M5 7 L8 10 L14 3"),
                SolveButton, Button("중지", ViewModel.CancelCommand, "M3 3 H13 V13 H3 Z", true)
            }
        };
        var toolbarPanel = new Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.Parse("#CBD5E1")), BorderThickness = new Thickness(0, 0, 0, 1), Child = toolbar };
        Grid.SetRow(toolbarPanel, 1); root.Children.Add(toolbarPanel);
        ModelEditor = Editor(false, 15);
        ModelEditor.Name = "ModelEditor";
        ModelEditor.Bind(TextBox.TextProperty, new Binding(nameof(ViewModel.EditorText)) { Mode = BindingMode.TwoWay });
        ModelEditor.Bind(IsEnabledProperty, new Binding(nameof(Workspace.CanUseFiles)) { Source = Workspace });
        ResultOutput = Editor(true, 13);
        ResultOutput.Name = "LegacyOutput";
        ResultOutput.Bind(TextBox.TextProperty, new Binding(nameof(ViewModel.LegacyKoptOutputText)));
        var editors = EditorOutputGrid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,6,*") };
        editors.ColumnDefinitions[0].MinWidth = 220;
        editors.ColumnDefinitions[2].MinWidth = 220;
        editors.Children.Add(Panel("모델 편집기", ModelEditor));
        AddSplitter(editors, true, 1);
        var legacy = Panel("원본 K-OPT 형식 출력", ResultOutput, true);
        Grid.SetColumn(legacy, 2); editors.Children.Add(legacy);
        var overview = new StackPanel { Spacing = 10 };
        var problem = new StackPanel { Spacing = 8 };
        problem.Children.Add(new TextBlock { Text = "문제 개요", FontSize = 16, FontWeight = FontWeight.SemiBold });
        problem.Children.Add(BoundText(nameof(ViewModel.OverviewText)));
        overview.Children.Add(new Border
        {
            Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.Parse("#CBD5E1")),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(14), Child = problem
        });
        var objectiveCard = new StackPanel { Spacing = 4 };
        objectiveCard.Children.Add(new TextBlock { Text = "목적값", Foreground = new SolidColorBrush(Color.Parse("#475569")) });
        var objective = BoundText(nameof(ViewModel.ObjectiveText));
        objective.FontSize = 28; objective.FontWeight = FontWeight.Bold;
        objectiveCard.Children.Add(objective);
        var alternative = BoundText(nameof(ViewModel.AlternativeSummaryText));
        alternative.Foreground = new SolidColorBrush(Color.Parse("#B45309")); alternative.FontWeight = FontWeight.SemiBold;
        objectiveCard.Children.Add(alternative);
        overview.Children.Add(new Border
        {
            Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.Parse("#CBD5E1")),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(14), Child = objectiveCard
        });
        overview.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.Parse("#EFF6FF")), BorderBrush = new SolidColorBrush(Color.Parse("#BFDBFE")),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(12),
            Child = new TextBlock { Text = "기본 변수 범위는 0 이상입니다.\n" + (OperatingSystem.IsMacOS() ? "(Fn+)F5 또는 ⌘Enter" : "F5 또는 Ctrl+Enter") + "로 실행합니다.", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#1E3A8A")) }
        });
        var workspaceGrid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("3*,8,*"), Margin = new Thickness(10, 8, 10, 0) };
        workspaceGrid.ColumnDefinitions[0].MinWidth = 600;
        workspaceGrid.ColumnDefinitions[2].MinWidth = 260;
        workspaceGrid.Children.Add(editors);
        AddSplitter(workspaceGrid, true, 1);
        var overviewPanel = new ScrollViewer { Content = overview, AllowAutoHide = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        Grid.SetColumn(overviewPanel, 2); workspaceGrid.Children.Add(overviewPanel);
        Grid.SetRow(workspaceGrid, 2); root.Children.Add(workspaceGrid);
        AddSplitter(root, false, 3);
        Graph = new SolutionGraph { MinHeight = 210, Name = "SolutionGraph" };
        Graph.Bind(SolutionGraph.ModelProperty, new Binding(nameof(ViewModel.LatestModel)));
        Graph.Bind(SolutionGraph.ResultProperty, new Binding(nameof(ViewModel.LatestResult)));
        Graph.Bind(SolutionGraph.UseKoreanTermsProperty, new Binding(nameof(ViewModel.UseKoreanResultTerms)));
        Graph.Bind(SolutionGraph.ShowInterceptsProperty, new Binding(nameof(ViewModel.ShowGraphIntercepts)));
        Graph.Bind(SolutionGraph.DecimalPlacesProperty, new Binding(nameof(ViewModel.DecimalPlaces)));
        VariablesGrid = Table(ViewModel.Variables,
            new("이름", "Name", "Name"), new("변수 유형", "Type", "Type"),
            new("최적값", "Value", "Value", true, "계산 결과의 변수값입니다."),
            new("수정비용", "Reduced cost", "ReducedCost", true, "변수의 축소비용입니다."));
        var constraints = Table(ViewModel.Constraints,
            new("이름", "Name", "Name"), new("관계식", "Relation", "Relation"),
            new("좌변 계산값", "Left-hand Side Value", "Activity", true), new("우변", "RHS", "RightHandSide", true),
            new("여유·잉여", "Slack", "Slack", true), new("쌍대값(잠재가격)", "Dual (Shadow Price)", "Dual", true));
        var diagnostics = Table(ViewModel.Diagnostics,
            new("코드", "Code", "Code"), new("수준", "Severity", "Severity"), new("행", "Line", "Span.Line"),
            new("열", "Column", "Span.Column"), new("메시지", "Message", "Message"), new("수정 힌트", "Hint", "Hint"));
        var objectiveRanges = Table(ViewModel.ObjectiveSensitivity,
            new("변수", "Variable", "Name"), new("현재계수", "Current", "CurrentValue", true),
            new("하한", "Lower", "LowerValue", true), new("상한", "Upper", "UpperValue", true));
        var rhsRanges = Table(ViewModel.RightHandSideSensitivity,
            new("제약식", "Constraint", "Name"), new("현재우변", "Current RHS", "CurrentValue", true),
            new("하한", "Lower", "LowerValue", true), new("상한", "Upper", "UpperValue", true));
        var optimalRanges = Table(ViewModel.OptimalRanges,
            new("변수", "Variable", "Name"), new("최솟값", "Minimum", "Minimum", true),
            new("최댓값", "Maximum", "Maximum", true), new("범위", "Range", "Range"));
        var representatives = AlternativeSolutionsGrid = Table(ViewModel.AlternativeSolutions, new("번호", "No.", "Number"), new("대표해", "Solution", "Values"));
        representatives.Columns[0].Width = new DataGridLength(85);
        representatives.Columns[1] = new DataGridTemplateColumn
        {
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            CellTemplate = new FuncDataTemplate<RepresentativeSolutionDisplayRow>((row, _) => new ScrollViewer
            {
                MaxHeight = 130, AllowAutoHide = false,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = new TextBlock { Text = row?.Values, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) }
            })
        };
        GraphScrollViewer = new ScrollViewer
        {
            Content = Graph, AllowAutoHide = false, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch
        };
        ResultsTabs = new TabControl
        {
            Margin = new Thickness(10, 0, 10, 10), Padding = new Thickness(0), Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.Parse("#CBD5E1")), BorderThickness = new Thickness(1),
            ItemsSource = new[]
            {
                Tab("진단", diagnostics), Tab("변수 해", VariablesGrid), Tab("제약식", constraints),
                Tab("민감도 분석", AnalysisPanel(nameof(ViewModel.SensitivitySummaryText), "목적식 계수", objectiveRanges, "제약식 우변", rhsRanges)),
                Tab("그래프", GraphScrollViewer),
                Tab("복수 최적해", AnalysisPanel(nameof(ViewModel.AlternativeSummaryText), "변수별 최적 범위", optimalRanges, "대표 최적해", representatives)),
                Tab("로그", new ListBox { ItemsSource = ViewModel.Logs })
            }
        };
        Grid.SetRow(ResultsTabs, 4); root.Children.Add(ResultsTabs);
        var status = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto"), Margin = new Thickness(10, 4) };
        status.Children.Add(BoundText(nameof(ViewModel.EncodingText)));
        var statusText = BoundText(nameof(ViewModel.StatusText)); statusText.Margin = new Thickness(16, 0);
        Grid.SetColumn(statusText, 1); status.Children.Add(statusText);
        var solverVersion = new TextBlock { Text = "HiGHS 1.15.1", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(solverVersion, 2); status.Children.Add(solverVersion);
        var statusPanel = new Border { Background = new SolidColorBrush(Color.Parse("#0F172A")), Child = status };
        foreach (var text in status.Children.OfType<TextBlock>()) text.Foreground = Brushes.White;
        Grid.SetRow(statusPanel, 5); root.Children.Add(statusPanel);
        Content = root;
        AddShortcut(Key.N, newCommand); AddShortcut(Key.O, openCommand); AddShortcut(Key.S, saveCommand);
        AddShortcut(Key.S, saveAsCommand, true); AddShortcut(Key.B, ViewModel.ValidateCommand, true);
        AddShortcut(Key.Enter, ViewModel.RunCommand);
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.F5), Command = ViewModel.RunCommand });
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.F5, KeyModifiers.Shift), Command = ViewModel.CancelCommand });
        Workspace.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(Workspace.Title)) Title = Workspace.Title;
            if (args.PropertyName == nameof(Workspace.CanUseFiles))
                foreach (var command in fileCommands.OfType<AsyncRelayCommand>()) command.RaiseCanExecuteChanged();
        };
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(ViewModel.UseKoreanResultTerms) or nameof(ViewModel.DecimalPlaces) or nameof(ViewModel.ShowGraphIntercepts)) RefreshSettings();
        };
        Opened += async (_, _) => await Workspace.InitializeAsync();
        Closing += async (_, args) =>
        {
            if (allowClose) return;
            args.Cancel = true;
            if (checkingClose) return;
            checkingClose = true;
            try
            {
                if (await Workspace.CanCloseAsync()) Dispatcher.UIThread.Post(() => { allowClose = true; Close(); });
            }
            finally { checkingClose = false; }
        };
        RefreshSettings();
    }

    internal static KeyModifiers PrimaryModifier(bool mac) => mac ? KeyModifiers.Meta : KeyModifiers.Control;
    private void AddShortcut(Key key, ICommand command, bool shift = false) => KeyBindings.Add(new KeyBinding
    { Gesture = new KeyGesture(key, PrimaryModifier(OperatingSystem.IsMacOS()) | (shift ? KeyModifiers.Shift : KeyModifiers.None)), Command = command });
    private ICommand FileCommand(Func<Task> action)
    {
        var command = new AsyncRelayCommand(action, () => Workspace.CanUseFiles);
        fileCommands.Add(command);
        return command;
    }
    private static MenuItem Menu(string text, ICommand command) => new() { Header = text, Command = command };
    private static Button Button(string text, ICommand command, string geometry, bool filled = false)
    {
        var button = new Button
        {
            Command = command, Classes = { "toolbar-action" }, Padding = new Thickness(10, 5), MinHeight = 32,
            Background = Brushes.Transparent, Foreground = new SolidColorBrush(Color.Parse("#334155")),
            BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5)
        };
        AutomationProperties.SetName(button, text);
        ToolTip.SetTip(button, text);
        var icon = new Avalonia.Controls.Shapes.Path
        {
            Data = StreamGeometry.Parse(geometry), Width = 14, Height = 14, Stretch = Stretch.Uniform,
            StrokeThickness = filled ? 0 : 1.5, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
            VerticalAlignment = VerticalAlignment.Center
        };
        icon.Bind(filled ? Avalonia.Controls.Shapes.Shape.FillProperty : Avalonia.Controls.Shapes.Shape.StrokeProperty,
            new Binding(nameof(ContentPresenter.Foreground)) { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor) { AncestorType = typeof(ContentPresenter) } });
        button.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 7,
            Children = { icon, new TextBlock { Text = text, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center } }
        };
        if (text == "중지")
        {
            button.Classes.Add("stop");
            button.Foreground = new SolidColorBrush(Color.Parse("#B91C1C"));
        }
        return button;
    }
    private static TextBox Editor(bool readOnly, int fontSize)
    {
        var editor = new TextBox
        {
            AcceptsReturn = true, AcceptsTab = !readOnly, IsReadOnly = readOnly, Classes = { "workspace-editor" },
            FontFamily = new FontFamily("Cascadia Mono,Consolas,Menlo,monospace"), FontSize = fontSize,
            TextWrapping = TextWrapping.NoWrap, Padding = new Thickness(0), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(0),
            Background = readOnly ? new SolidColorBrush(Color.Parse("#F8FAFC")) : Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch
        };
        // Keep the platform's text selection and IME support, without a second frame inside the panel.
        editor.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);
        ScrollViewer.SetHorizontalScrollBarVisibility(editor, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(editor, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ScrollViewer.SetAllowAutoHide(editor, false);
        return editor;
    }
    private static Border Panel(string title, Control content, bool compatibility = false)
    {
        var grid = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,*") };
        var header = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"), Background = new SolidColorBrush(Color.Parse(compatibility ? "#EFF6FF" : "#F8FAFC")) };
        header.Children.Add(new TextBlock { Text = title, Margin = new Thickness(10, 6), FontWeight = FontWeight.SemiBold });
        if (compatibility)
        {
            var badge = new TextBlock { Text = "호환 보기", Foreground = new SolidColorBrush(Color.Parse("#1D4ED8")), FontSize = 11, Margin = new Thickness(6, 6, 10, 6), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(badge, 1); header.Children.Add(badge);
        }
        grid.Children.Add(header);
        Grid.SetRow(content, 1); grid.Children.Add(content);
        return new Border { Child = grid, Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.Parse("#CBD5E1")), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), ClipToBounds = true };
    }
    private void ApplyWorkspaceStyles()
    {
        // Keep a fixed inset around the text viewport, with scrollbar tracks flush to the panel edges.
        Styles.Add(new Style(s => s.OfType<TextBox>().Class("workspace-editor")
            .Template().OfType<ScrollViewer>().Name("PART_ScrollViewer")
            .Template().OfType<ScrollContentPresenter>().Name("PART_ContentPresenter"))
        {
            Setters = { new Setter(MarginProperty, new Thickness(6)) }
        });
        ToolbarState(null, ":pointerover", "#F1F5F9", "#0F172A");
        ToolbarState(null, ":pressed", "#E2E8F0", "#0F172A");
        ToolbarState("primary", ":pointerover", "#1D4ED8", "#FFFFFF");
        ToolbarState("primary", ":pressed", "#1E40AF", "#FFFFFF");
        ToolbarState("stop", ":pointerover", "#FEF2F2", "#B91C1C");
        ToolbarState("stop", ":pressed", "#FEE2E2", "#991B1B");
        ToolbarState(null, ":disabled", "#FFFFFF", "#94A3B8");
        ToolbarState("primary", ":disabled", "#DBEAFE", "#93C5FD");
        Styles.Add(new Style(s => s.OfType<Button>().Class("toolbar-action").Class(":focus-visible").Template().OfType<ContentPresenter>())
        {
            Setters = { new Setter(BorderBrushProperty, new SolidColorBrush(Color.Parse("#60A5FA"))) }
        });
        Styles.Add(new Style(s => s.OfType<TabItem>().Class("result-tab"))
        {
            Setters = { new Setter(BackgroundProperty, new SolidColorBrush(Color.Parse("#F1F5F9"))), new Setter(ForegroundProperty, new SolidColorBrush(Color.Parse("#475569"))) }
        });
        Styles.Add(new Style(s => s.OfType<TabItem>().Class("result-tab").Class(":selected"))
        {
            Setters = { new Setter(BackgroundProperty, Brushes.White), new Setter(ForegroundProperty, new SolidColorBrush(Color.Parse("#0F172A"))), new Setter(FontWeightProperty, FontWeight.SemiBold) }
        });
        Styles.Add(new Style(s => s.OfType<TabItem>().Class("result-tab").Template().OfType<Border>().Name("PART_SelectedPipe"))
        {
            Setters = { new Setter(IsVisibleProperty, false) }
        });
        Styles.Add(new Style(s => s.OfType<MenuItem>())
        {
            Setters = { new Setter(FontSizeProperty, 13d), new Setter(PaddingProperty, new Thickness(10, 4)), new Setter(MinHeightProperty, 26d) }
        });
    }
    private void ToolbarState(string? role, string state, string background, string foreground)
    {
        Styles.Add(new Style(s =>
        {
            var selector = s.OfType<Button>().Class("toolbar-action");
            if (role is not null) selector = selector.Class(role);
            return selector.Class(state).Template().OfType<ContentPresenter>();
        })
        {
            Setters =
            {
                new Setter(BackgroundProperty, new SolidColorBrush(Color.Parse(background))),
                new Setter(ForegroundProperty, new SolidColorBrush(Color.Parse(foreground))),
                new Setter(BorderBrushProperty, Brushes.Transparent)
            }
        });
    }
    private static void AddSplitter(Grid grid, bool columns, int index)
    {
        var splitter = new GridSplitter { Background = Brushes.LightGray, ResizeDirection = columns ? GridResizeDirection.Columns : GridResizeDirection.Rows,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        if (columns) Grid.SetColumn(splitter, index); else Grid.SetRow(splitter, index);
        grid.Children.Add(splitter);
    }
    private TextBlock BoundText(string path)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap };
        text.Bind(TextBlock.TextProperty, new Binding(path));
        return text;
    }
    private static TabItem Tab(string title, Control content) => new()
    {
        Header = title, Content = content, Classes = { "result-tab" }, FontSize = 13, MinHeight = 28, Padding = new Thickness(10, 5),
        BorderBrush = new SolidColorBrush(Color.Parse("#CBD5E1")), BorderThickness = new Thickness(0, 0, 1, 1),
    };
    private Grid AnalysisPanel(string summary, string leftTitle, Control left, string rightTitle, Control right)
    {
        var grid = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,*"), ColumnDefinitions = ColumnDefinitions.Parse("*,8,*"), Margin = new Thickness(8), RowSpacing = 8 };
        var label = BoundText(summary); Grid.SetColumnSpan(label, 3); grid.Children.Add(label);
        var leftPanel = Panel(leftTitle, left); Grid.SetRow(leftPanel, 1); grid.Children.Add(leftPanel);
        var rightPanel = Panel(rightTitle, right); Grid.SetRow(rightPanel, 1); Grid.SetColumn(rightPanel, 2); grid.Children.Add(rightPanel);
        return grid;
    }
    private DataGrid Table(IEnumerable rows, params Column[] columns)
    {
        var grid = new DataGrid { ItemsSource = rows, AutoGenerateColumns = false, IsReadOnly = true, CanUserResizeColumns = true, MinColumnWidth = 85 };
        foreach (var column in columns)
        {
            BindingBase binding = new Binding(column.Path);
            if (column.Numeric) binding = new MultiBinding
            {
                Bindings = { new Binding(column.Path), new Binding(nameof(ViewModel.DecimalPlaces)) { Source = ViewModel } },
                Converter = new NumberConverter()
            };
            grid.Columns.Add(new DataGridTextColumn { Binding = binding, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        }
        resultGrids.Add((grid, columns));
        return grid;
    }
    private void RefreshSettings()
    {
        koreanTerms.IsChecked = ViewModel.UseKoreanResultTerms;
        intercepts.IsChecked = ViewModel.ShowGraphIntercepts;
        precision.Header = $"소수 자릿수: {ViewModel.DecimalPlaces}자리";
        foreach (var pair in precisionItems) pair.Item.IsChecked = pair.Digits == ViewModel.DecimalPlaces;
        foreach (var (grid, columns) in resultGrids)
            for (var i = 0; i < columns.Length; i++)
            {
                var column = columns[i];
                var header = new TextBlock { Text = ViewModel.UseKoreanResultTerms ? column.Korean : column.English, TextWrapping = TextWrapping.Wrap };
                ToolTip.SetTip(header, HeaderHelp(column.Path, ViewModel.UseKoreanResultTerms));
                grid.Columns[i].Header = header;
            }
    }
    private static TextBlock HeaderHelp(string path, bool korean)
    {
        var (description, detail) = path switch
        {
            "Value" => korean ? ("계산 결과에서 구한 변수의 최적값입니다.", "표시만 반올림하며 계산값의 정밀도는 유지됩니다.") : ("The variable value returned by the solver.", "Rounding affects display only."),
            "ReducedCost" => korean ? ("해당 변수가 최적해에 포함될 수 있도록 목적계수가 개선되어야 하는 정도입니다.", "연속 선형계획의 최적기저를 기준으로 해석합니다.") : ("The reduced cost of the variable.", "Interpret it using the optimal basis of a continuous LP."),
            "Activity" => korean ? ("최적 변수값을 제약식 좌변에 대입해 계산한 값입니다.", "") : ("The left-hand side evaluated at the solution.", ""),
            "RightHandSide" => korean ? ("제약식 오른쪽에 지정한 기준값입니다.", "") : ("The reference value on the right-hand side.", ""),
            "Slack" => korean ? ("현재 해가 제약 경계에서 얼마나 떨어져 있는지 나타냅니다.", "<=는 우변-좌변, >=는 좌변-우변이며 0이면 경계에서 만족됩니다.") : ("How far the solution is from the constraint boundary.", "RHS-LHS for <=; LHS-RHS for >=; zero means binding."),
            "Dual" => korean ? ("자원이 1 단위 증가할 때, 최적 목적 함수 값(이익 또는 비용)이 얼마나 변하는지를 나타내는 값", "연속 선형계획에서 다른 조건이 유지되는 국소 범위의 해석이며, 부호는 최대화·최소화와 제약 방향에 따라 달라집니다.") : ("Indicates how much the optimal objective value changes when the available resource increases by one unit.", "Interpret it locally for a continuous LP; the sign depends on the objective sense and constraint direction."),
            "LowerValue" or "UpperValue" => korean ? ("현재 최적기저에서 해당 항목 하나만 변경할 때의 허용 범위입니다.", "다른 목적계수와 우변은 고정한 것으로 해석합니다.") : ("The allowable range when changing this item alone.", "Other coefficients and right-hand sides stay fixed."),
            _ => korean ? ("모델과 계산 결과에 기록된 항목입니다.", "") : ("An item from the model or calculation result.", "")
        };
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 420 };
        text.Inlines!.Add(new Avalonia.Controls.Documents.Run(description) { FontWeight = path == "Dual" ? FontWeight.Bold : FontWeight.Normal });
        if (detail.Length > 0) { text.Inlines.Add(new Avalonia.Controls.Documents.LineBreak()); text.Inlines.Add(new Avalonia.Controls.Documents.Run(detail)); }
        return text;
    }
    private Task SaveDisplaySettingsAsync(int? digits = null) => Workspace.SetSettingsAsync(new UserSettings(koreanTerms.IsChecked, intercepts.IsChecked, digits ?? ViewModel.DecimalPlaces));
    private sealed record Column(string Korean, string English, string Path, bool Numeric = false, string? Help = null);
    private sealed class NumberConverter : IMultiValueConverter
    {
        public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
            values.Count == 2 && values[1] is int digits ? ResultNumberFormatter.Format(values[0] is double number ? number : null, digits) : "-";
    }
}
