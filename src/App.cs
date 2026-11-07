using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;

[assembly: AssemblyTitle("SpeedKlava")]
[assembly: AssemblyProduct("SpeedKlava")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace SpeedKlava
{
    public class App
    {
        Window window;
        Profile profile;
        Run run;
        Random random = new Random();
        DispatcherTimer timer;
        Stopwatch clock = new Stopwatch();
        bool running, paused, started, initializing = true, committed, expert;
        double previousTick, lastInputElapsed;
        int limit = 60;
        string page = "Тренировка";
        string smokeDirectory;
        Dictionary<char, Border> keys = new Dictionary<char, Border>();
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                var a = new Application();
                var app = new App();
                app.smokeDirectory = args.Length == 2 && args[0] == "--smoke-test" ? args[1] : null;
                if (app.smokeDirectory == null) app.profile = Store.Load(); else app.profile = new Profile();
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("SpeedKlava.Main.xaml")) app.window = (Window)XamlReader.Load(s);
                app.Setup();
                a.Run(app.window);
                return Environment.ExitCode;
            }
            catch (Exception ex)
            {
                try { Directory.CreateDirectory(Store.Folder); File.WriteAllText(Path.Combine(Store.Folder, "crash.log"), ex.ToString()); } catch { }
                if (args.Contains("--smoke-test")) { Console.Error.WriteLine(ex); return 1; }
                MessageBox.Show("Не удалось запустить SpeedKlava.\n" + ex.Message, "SpeedKlava", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }
        T Get<T>(string name) where T : FrameworkElement { return (T)window.FindName(name); }
        TextBlock Text(string name) { return Get<TextBlock>(name); }
        Brush Brush(string hex) { return (Brush)new BrushConverter().ConvertFromString(hex); }
        void Setup()
        {
            window.SourceInitialized += delegate {
                try { int dark = 1; IntPtr handle = new System.Windows.Interop.WindowInteropHelper(window).Handle; if (DwmSetWindowAttribute(handle, 20, ref dark, 4) != 0) DwmSetWindowAttribute(handle, 19, ref dark, 4); } catch { }
            };
            foreach (string nav in new[] { "Train", "Expert", "Lessons", "Progress", "Science", "Settings" })
            {
                string n = nav;
                Get<Button>("Nav" + n).Click += delegate { Navigate(n); };
            }
            Get<ComboBox>("LanguageBox").SelectedIndex = profile.Language == "RU" ? 0 : 1;
            Get<ComboBox>("LanguageBox").SelectionChanged += delegate {
                if (initializing) return;
                EndPartial(); profile.Language = Get<ComboBox>("LanguageBox").SelectedIndex == 0 ? "RU" : "EN";
                if (expert && Mode == Expert.Code && profile.Language == "RU") { initializing = true; Get<ComboBox>("ModeBox").SelectedIndex = 0; initializing = false; }
                Save(); Refresh(); Prepare(); if (!TrainingPage) RenderOther();
            };
            Get<ComboBox>("ModeBox").SelectionChanged += delegate { if (!initializing) { EndPartial(); if (expert && Mode == Expert.Code && profile.Language != "EN") { Get<ComboBox>("LanguageBox").SelectedIndex = 1; return; } Prepare(); } };
            Get<ComboBox>("DurationBox").SelectionChanged += delegate { if (!initializing) { EndPartial(); Prepare(); } };
            UpdateGoalSelection();
            Get<ComboBox>("ExpertGoalBox").SelectionChanged += delegate { if (!initializing) { EndPartial(); profile.ExpertGoal = int.Parse(((ComboBoxItem)Get<ComboBox>("ExpertGoalBox").SelectedItem).Content.ToString()); Save(); Prepare(); } };
            Get<Button>("StartButton").Click += delegate { if (running) { EndPartial(); Prepare(); } Start(); };
            Get<Button>("PauseButton").Click += delegate { TogglePause(); };
            window.PreviewTextInput += OnText;
            window.PreviewKeyDown += OnKey;
            Get<Border>("TypingSurface").MouseDown += delegate { if (running && !paused) Get<Border>("TypingSurface").Focus(); };
            window.Deactivated += delegate { if (running && !paused) TogglePause(); };
            window.Closing += delegate { EndPartial(); if (smokeDirectory == null) Save(); };
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) }; timer.Tick += Tick; timer.Start();
            initializing = false;
            Refresh(); Prepare(); HighlightNav("Train");
            if (Store.Warning != "") Notice(Store.Warning);
            if (smokeDirectory != null) window.ContentRendered += delegate { Smoke(); };
        }
        string Mode { get { return ((ComboBoxItem)Get<ComboBox>("ModeBox").SelectedItem).Content.ToString(); } }
        bool TrainingPage { get { return page == "Тренировка" || page == "Для опытных"; } }
        bool ShowHints { get { return profile.Hints && Mode != "Тест" && !expert; } }
        void UpdateGoalSelection()
        {
            var box = Get<ComboBox>("ExpertGoalBox");
            var item = box.Items.Cast<ComboBoxItem>().OrderBy(i => Math.Abs(int.Parse(i.Content.ToString()) - profile.ExpertGoal)).First();
            box.SelectedItem = item;
        }
        void Save()
        {
            if (smokeDirectory != null) return;
            try { Store.Save(profile); } catch (Exception e) { Notice("Прогресс остаётся в памяти, но не записан на диск: " + e.Message + " Экспортируй его в настройках."); }
        }
        void Notice(string message) { Text("Notice").Text = message; Text("Notice").Visibility = Visibility.Visible; }
        void Refresh()
        {
            var recent = profile.Sessions.Where(s => s.Language == profile.Language && s.Completed).Reverse().Take(10).ToList();
            var tests = profile.Sessions.Where(s => s.Language == profile.Language && s.Mode == "Тест" && s.Completed && s.Seconds >= 20).ToList();
            if (expert)
            {
                recent = profile.Sessions.Where(s => s.Language == profile.Language && s.Mode == Mode && s.Completed).Reverse().Take(10).ToList();
                tests = profile.Sessions.Where(s => s.Language == profile.Language && s.Mode == Mode && s.Completed && s.RequestedSeconds == limit && s.Seconds >= limit - 1 && s.Accuracy >= 97).ToList();
            }
            Text("BestCaption").Text = expert ? "РЕКОРД ЭТОГО РЕЖИМА" : "ЛУЧШИЙ ТЕСТ";
            Text("BestHint").Text = expert ? "Та же длительность · точность ≥97%" : "Тесты отдельно от учебных повторов";
            Text("BestStat").Text = tests.Count == 0 ? "— зн/мин" : Math.Round(tests.Max(s => s.Cpm)) + " зн/мин";
            int attempts = recent.Sum(s => s.Attempts);
            Text("AccuracyStat").Text = attempts == 0 ? "—" : (100.0 * recent.Sum(s => s.Correct) / attempts).ToString("0.0") + "%";
            Text("StreakStat").Text = profile.Streak == 0 ? "Первый шаг" : profile.Streak + " дн. подряд";
            Text("GoalLabel").Text = (profile.TodaySeconds / 60).ToString("0.0") + " / " + profile.GoalMinutes + " минут сегодня";
            Get<ProgressBar>("GoalBar").Value = Math.Min(100, profile.TodaySeconds / (profile.GoalMinutes * 60) * 100);
            Text("LevelLabel").Text = "Уровень " + (profile.Xp / 500 + 1) + " · " + new[] { "Исследователь", "Практик", "В потоке", "Мастер ритма" }[Math.Min(3, profile.Xp / 1500)];
            Text("XpLabel").Text = profile.Xp % 500 + " / 500 XP"; Get<ProgressBar>("LevelBar").Value = profile.Xp % 500;
            Text("LessonTitle").Text = expert ? "Следующий рубеж: " + profile.ExpertGoal + " зн/мин" : (profile.Stage + 1).ToString("00") + " / " + Curriculum.Names[profile.Stage];
            Text("LessonHint").Text = expert ? "Личный темп без гонки за ошибками. Сохраняй точность от 97%." : profile.Language == "RU" ? "Найди выступы на А и О. Держи пальцы близко к ФЫВА и ОЛДЖ." : "Найди выступы на F и J. Держи пальцы близко к ASDF и JKL;.";
        }
        void Prepare()
        {
            limit = new[] { 30, 60, 120, 300 }[Get<ComboBox>("DurationBox").SelectedIndex];
            run = new Run(expert ? Expert.Generate(profile, Mode, random, Math.Max(2500, limit * 30)) : Curriculum.Generate(profile, Mode, random, 210), profile, Mode);
            run.RequestedSeconds = limit; run.TargetCpm = expert ? profile.ExpertGoal : 0;
            committed = false; running = false; paused = false; started = false; clock.Reset();
            lastInputElapsed = 0;
            Get<Border>("ExpertPanel").Visibility = expert ? Visibility.Visible : Visibility.Collapsed;
            Get<Border>("LessonBanner").Visibility = expert ? Visibility.Collapsed : Visibility.Visible;
            Get<Border>("KeyboardCard").Visibility = expert ? Visibility.Collapsed : Visibility.Visible;
            Text("ExpertDescription").Text = expert ? Expert.Description(Mode) : "";
            Text("ExpertFeedback").Text = "Цель " + profile.ExpertGoal + " зн/мин · таймер начнётся с первой буквы.";
            Get<Border>("ResultPanel").Visibility = Visibility.Collapsed;
            Get<Button>("StartButton").Content = "Начать практику  →";
            Get<Button>("PauseButton").IsEnabled = false;
            Get<Button>("PauseButton").Content = "Пауза";
            Get<ProgressBar>("RunBar").Value = 0;
            Text("RunStats").Text = "ГОТОВ К ПРАКТИКЕ · " + limit + " с";
            Text("Coach").Text = expert ? "Сохраняй точность и прибавляй в удобном темпе." : Mode == "Тест" ? "Новый текст без подсказок. Результат — отдельно от курса." : "Не смотри на клавиатуру. Начни с удобного темпа.";
            Refresh(); RenderKeyboard(); RenderText();
        }
        void Start()
        {
            if (run == null || committed) Prepare();
            running = true; paused = false; started = false;
            Get<Button>("StartButton").Content = "Заново"; Get<Button>("PauseButton").IsEnabled = true;
            Text("Coach").Text = "Таймер начнётся с первой буквы. Esc — пауза.";
            Get<Border>("TypingSurface").Focus();
        }
        void TogglePause()
        {
            if (!running) return;
            if (!paused) { SyncTime(); clock.Stop(); run.BreakRhythm(); paused = true; Get<Button>("PauseButton").Content = "Продолжить"; Text("Coach").Text = "Пауза. Время остановлено. Продолжай, когда будешь готов."; }
            else { paused = false; if (started) clock.Start(); previousTick = clock.Elapsed.TotalSeconds; lastInputElapsed = previousTick; Get<Button>("PauseButton").Content = "Пауза"; Text("Coach").Text = "Возвращайся к спокойному ритму."; Get<Border>("TypingSurface").Focus(); }
        }
        void OnKey(object sender, KeyEventArgs e)
        {
            if (!TrainingPage || !running) return;
            if (e.Key == Key.Escape) { TogglePause(); e.Handled = true; }
            if (e.Key == Key.Back && Get<Border>("TypingSurface").IsKeyboardFocused) { e.Handled = true; Text("Coach").Text = "Ошибочная буква не продвигает курсор. Просто нажми верную."; }
            if (e.Key == Key.Space && !paused && Get<Border>("TypingSurface").IsKeyboardFocused) { Accept(' '); e.Handled = true; }
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0 && Get<Border>("TypingSurface").IsKeyboardFocused) { e.Handled = true; }
        }
        void OnText(object sender, TextCompositionEventArgs e)
        {
            if (!TrainingPage || !running || paused || !Get<Border>("TypingSurface").IsKeyboardFocused || e.Text.Length != 1) return;
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0) return;
            if (!char.IsControl(e.Text[0])) { Accept(e.Text[0]); e.Handled = true; }
        }
        void Accept(char c)
        {
            if (!running || paused) return;
            char expected = run.Text[run.Position];
            if (char.IsLetter(c) && ((profile.Language == "RU" && c < 128) || (profile.Language == "EN" && c >= 0x400 && c <= 0x4ff)))
            { Text("Coach").Text = "Смени системную раскладку Windows на " + profile.Language + " (Win + Пробел). Это нажатие не засчитано."; return; }
            if (!started) { started = true; clock.Start(); previousTick = 0; }
            SyncTime();
            if (run.ActiveSeconds >= limit) { Finish(true); return; }
            bool good = run.Input(c);
            lastInputElapsed = clock.Elapsed.TotalSeconds;
            Text("Coach").Text = good ? "Хорошо. Читай чуть вперёд и сохраняй ритм." : "Спокойно: нужна «" + (expected == ' ' ? "пробел" : expected.ToString()) + "». Нажми её, чтобы продолжить.";
            RenderText(); UpdateRunStats();
            if (run.Finished) Finish(true);
        }
        void SyncTime()
        {
            if (!started || paused) return;
            double now = clock.Elapsed.TotalSeconds;
            double step = Math.Max(0, now - previousTick);
            run.ActiveSeconds = Math.Min(limit, run.ActiveSeconds + step); previousTick = now;
        }
        void Tick(object sender, EventArgs e)
        {
            if (!running || paused || !started) return;
            SyncTime(); UpdateRunStats();
            if (run.ActiveSeconds >= limit) { Finish(true); return; }
            if (clock.Elapsed.TotalSeconds - lastInputElapsed > 15) { TogglePause(); Text("Coach").Text = "Пауза после 15 секунд без ввода. Нажми «Продолжить»."; }
        }
        void UpdateRunStats()
        {
            Text("RunStats").Text = profile.LiveStats ? Math.Round(run.Cpm) + " зн/мин   ·   " + run.Accuracy.ToString("0.0") + "%   ·   " + Math.Ceiling(limit - run.ActiveSeconds) + " с" : Math.Ceiling(limit - run.ActiveSeconds) + " с";
            Get<ProgressBar>("RunBar").Value = 100 * Math.Max(run.ActiveSeconds / limit, (double)run.Position / run.Text.Length);
            if (expert)
            {
                double delta = run.Correct - run.ActiveSeconds * run.TargetCpm / 60;
                Text("ExpertFeedback").Text = profile.LiveStats ? "Темп цели: " + run.TargetCpm + " зн/мин · от ориентира " + delta.ToString("+0;-0;0") + " знаков · " + (run.RhythmSamples >= 10 ? "разброс ритма " + run.RhythmCv.ToString("0") + "% (меньше — ровнее)" : "собираем данные о ритме") : "Цель " + run.TargetCpm + " зн/мин · подробные метрики после занятия.";
            }
        }
        void Finish(bool completed)
        {
            if (committed || run == null || run.Attempts == 0) { running = false; clock.Stop(); return; }
            SyncTime(); clock.Stop(); running = false; paused = false; committed = true;
            int oldStage = profile.Stage;
            double baseline = expert ? Expert.Baseline(profile, Mode, profile.Language, limit) : 0;
            Session s = run.Commit(profile, completed); Save(); Refresh();
            Get<Button>("PauseButton").IsEnabled = false; Get<Button>("PauseButton").Content = "Пауза";
            Get<Button>("StartButton").Content = "Следующее занятие  →";
            Get<Border>("ResultPanel").Visibility = Visibility.Visible;
            Text("ResultTitle").Text = completed ? (profile.Stage > oldStage ? "Новый этап открыт!  +" : "Ещё один шаг вперёд.  +") + s.Xp + " XP" : "Практика сохранена";
            string advice = s.Accuracy >= 97 ? "Точность отличная. Сохраняй этот ритм." : "В следующем занятии немного снизь темп. Сначала точность, затем скорость.";
            if (Mode == "Курс" && profile.Stage == oldStage && s.Accuracy >= 97) advice += " Для открытия этапа: два занятия подряд, ≥97% и ≥80 верных знаков в каждом.";
            if (expert)
            {
                bool full = completed && s.Seconds >= limit - 1;
                advice = !full ? "Для сравнения результатов заверши полную выбранную длительность." : s.Accuracy < 97 ? "Скорость пока не идёт в рекорд: верни точность к 97%, затем повышай темп." : s.Cpm >= s.TargetCpm ? "Личный рубеж пройден с точностью ≥97%! Закрепи результат перед повышением цели." : "До цели " + Math.Ceiling(s.TargetCpm - s.Cpm) + " зн/мин. Сохраняй точность и прибавляй постепенно.";
                if (full && s.Accuracy >= 97) advice += baseline > 0 ? " К медиане последних пяти подходящих занятий: " + (s.Cpm - baseline).ToString("+0;-0;0") + " зн/мин." : " Это первый зачётный результат для этого режима и длительности.";
                if (s.RhythmSamples >= 10) advice += " Разброс интервалов: " + s.RhythmCv.ToString("0") + "%; меньше — ровнее. Сравнивай в одном режиме.";
                if (full && s.Accuracy >= 97 && s.Cpm >= s.TargetCpm) Text("ResultTitle").Text = "Личный рубеж пройден!  +" + s.Xp + " XP";
            }
            if (profile.TodaySeconds >= profile.GoalMinutes * 60) advice += " Дневная цель выполнена. Можно отдохнуть!";
            Text("ResultDetail").Text = Math.Round(s.Cpm) + " зн/мин  ·  " + s.Wpm.ToString("0.0") + " WPM  ·  " + s.Accuracy.ToString("0.0") + "% точности  ·  " + s.Errors + " ошибок\n" + advice;
            Text("Coach").Text = "Сделай короткую паузу. Посмотри вдаль и расслабь руки.";
            UpdateRunStats();
        }
        void EndPartial() { if (running && !committed && run.Attempts > 0) Finish(false); running = false; clock.Stop(); }
        void RenderText()
        {
            TextBlock target = Text("TypingText"); target.FontSize = profile.LargeText ? 30 : 25; target.Inlines.Clear();
            int start = 0;
            if (run.Position > 100) { start = run.Text.LastIndexOf(' ', Math.Max(0, run.Position - 60)); if (start < 0) start = 0; else start++; }
            int end = Math.Min(run.Text.Length, start + 170);
            for (int i = start; i < end; i++)
            {
                var r = new System.Windows.Documents.Run(run.Text[i].ToString());
                if (i < run.Position) r.Foreground = Brush("#80D9B5");
                else if (i == run.Position) { r.Background = Brush(run.LastError ? "#9B4059" : "#B4A0FF"); r.Foreground = Brush("#14121D"); r.TextDecorations = TextDecorations.Underline; }
                else r.Foreground = Brush("#B3B6C9");
                target.Inlines.Add(r);
            }
            if (run.Position < run.Text.Length)
            {
                char c = run.Text[run.Position];
                Text("FingerHint").Text = ShowHints ? (c == ' ' ? "Пробел" : c.ToString()) + " → " + Curriculum.Finger(c, profile.Language) + (char.IsUpper(c) || "!?".Contains(c) ? " · Shift другой рукой" : "") : expert ? "Полная раскладка · без подсказок пальцев" : "Подсказки скрыты · взгляд на текст";
                foreach (var k in keys) { bool active = Curriculum.PhysicalKey(c, profile.Language) == k.Key && ShowHints; k.Value.BorderThickness = new Thickness(active ? 2 : 1); k.Value.BorderBrush = Brush(active ? "#D6CAFF" : "#393C4B"); k.Value.Background = Brush(active ? "#544474" : "#242631"); }
            }
        }
        void RenderKeyboard()
        {
            var panel = Get<StackPanel>("KeyboardPanel"); panel.Children.Clear(); keys.Clear();
            string[] rows = profile.Language == "RU" ? new[] { "1234567890", "йцукенгшщзхъ", "фывапролджэ", "ячсмитьбю.", " " } : new[] { "1234567890", "qwertyuiop[]", "asdfghjkl;'", "zxcvbnm,./", " " };
            string[] colors = { "#EAA7BB", "#E5C38B", "#83CFC9", "#A5B5F1" };
            foreach (string row in rows)
            {
                var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                foreach (char c in row)
                {
                    string finger = Curriculum.Finger(c, profile.Language);
                    int idx = finger.Contains("мизинец") ? 0 : finger.Contains("безымянный") ? 1 : finger.Contains("средний") ? 2 : 3;
                    var label = new TextBlock { Text = c == ' ' ? "п р о б е л" : c.ToString().ToUpperInvariant(), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush(colors[idx]), FontSize = 13 };
                    var border = new Border { Child = label, Width = c == ' ' ? 260 : 39, Height = 33, CornerRadius = new CornerRadius(6), Margin = new Thickness(3), Background = Brush("#242631"), BorderBrush = Brush("#393C4B"), BorderThickness = new Thickness(1), ToolTip = finger };
                    keys[c] = border; line.Children.Add(border);
                }
                panel.Children.Add(line);
            }
            // The rare Russian key is outside the main letter block.
            if (profile.Language == "RU")
            {
                var row = (StackPanel)panel.Children[0];
                var b = new Border { Child = new TextBlock { Text = "Ё", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush(colors[0]) }, Width = 39, Height = 33, Margin = new Thickness(3), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), BorderBrush = Brush("#393C4B"), Background = Brush("#242631"), ToolTip = "Левый мизинец" };
                row.Children.Insert(0, b); keys['ё'] = b;
            }
            panel.Visibility = ShowHints ? Visibility.Visible : Visibility.Collapsed;
            Text("KeyboardCaption").Text = ShowHints ? (profile.Language == "RU" ? "Ф Ы В А  ·  О Л Д Ж — исходная позиция. Ё находится слева от 1." : "A S D F  ·  J K L ; — исходная позиция.") + " Для заглавных — Shift другой рукой." : expert ? "Читай на слово вперёд. Если сбился — снизь темп и восстанови точность." : "Подсказки выключены. Ориентируйся по выступам на А/О или F/J.";
        }
        void HighlightNav(string nav)
        {
            foreach (string n in new[] { "Train", "Expert", "Lessons", "Progress", "Science", "Settings" }) Get<Button>("Nav" + n).Background = Brush(n == nav ? "#302A43" : "#15161E");
        }
        void Navigate(string nav)
        {
            if (running && !paused) TogglePause();
            if ((nav == "Train" || nav == "Expert") && expert != (nav == "Expert"))
            {
                EndPartial(); expert = nav == "Expert";
                initializing = true;
                var modes = Get<ComboBox>("ModeBox"); modes.Items.Clear();
                foreach (string mode in expert ? Expert.Modes : new[] { "Курс", "Слабые клавиши", "Сочетания", "Тексты", "Тест" }) modes.Items.Add(new ComboBoxItem { Content = mode });
                modes.SelectedIndex = 0; initializing = false; Prepare();
            }
            page = new Dictionary<string, string> { { "Train", "Тренировка" }, { "Expert", "Для опытных" }, { "Lessons", "Карта навыка" }, { "Progress", "Мой прогресс" }, { "Science", "Как это работает" }, { "Settings", "Настройки" } }[nav];
            HighlightNav(nav); Text("PageTitle").Text = nav == "Train" ? "Найди свой ритм." : page;
            Text("PageSubtitle").Text = nav == "Train" ? "Точность сегодня. Скорость — следом." : new Dictionary<string, string> { { "Expert", "Ты уже знаешь клавиши. Теперь — скорость, ритм и сложные переходы." }, { "Lessons", "От первых движений до свободной печати." }, { "Progress", "Твой единственный соперник — ты вчерашний." }, { "Science", "Что говорят исследования и как устроены занятия." }, { "Settings", "Сделай практику удобной для себя." } }[nav];
            Get<StackPanel>("TrainPage").Visibility = TrainingPage ? Visibility.Visible : Visibility.Collapsed;
            Get<StackPanel>("OtherPage").Visibility = TrainingPage ? Visibility.Collapsed : Visibility.Visible;
            if (!TrainingPage) RenderOther();
        }
        StackPanel Card(string heading, string detail)
        {
            var p = new StackPanel();
            p.Children.Add(new TextBlock { Text = heading, FontSize = 19, FontWeight = FontWeights.SemiBold });
            if (detail != "") p.Children.Add(new TextBlock { Text = detail, Foreground = Brush("#A8ABBF"), Margin = new Thickness(0, 10, 0, 0), LineHeight = 23 });
            Get<StackPanel>("OtherPage").Children.Add(new Border { Style = (Style)window.Resources["Card"], Child = p, Margin = new Thickness(0, 0, 0, 15) });
            return p;
        }
        void Link(StackPanel panel, string label, string url)
        {
            var b = new Button { Content = label + "  ↗", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 13, 0, 0), Foreground = Brush("#C6B5FF") };
            b.Click += delegate { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception e) { Notice(e.Message); } }; panel.Children.Add(b);
        }
        void RenderOther()
        {
            Get<StackPanel>("OtherPage").Children.Clear();
            if (page == "Карта навыка") Lessons();
            if (page == "Мой прогресс") Progress();
            if (page == "Как это работает") Science();
            if (page == "Настройки") Settings();
        }
        void Lessons()
        {
            Card("Точность открывает следующий шаг", "Два занятия курса подряд с точностью от 97% и минимум 80 верными знаками в каждом открывают следующий этап. Это правило SpeedKlava, а не научно установленная граница. Скорость не ограничивает переход.");
            for (int i = 0; i < Curriculum.Count; i++)
            {
                int stage = i;
                var p = Card((i + 1).ToString("00") + "  /  " + Curriculum.Names[i] + (i < profile.Stage ? "   ✓" : i == profile.Stage ? "   ← сейчас" : ""), "Клавиши: " + (profile.Language == "RU" ? Curriculum.Ru : Curriculum.En)[i]);
                var b = new Button { Content = i > profile.Stage ? "Откроется после предыдущего этапа" : i == profile.Stage ? "Продолжить" : "Повторить без изменения этапа", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0), IsEnabled = i <= profile.Stage };
                b.Click += delegate {
                    Navigate("Train"); EndPartial(); Get<ComboBox>("ModeBox").SelectedIndex = 0;
                    Prepare();
                    if (stage < profile.Stage)
                    {
                        var copy = new Profile { Language = profile.Language, RuStage = stage, EnStage = stage, Keys = profile.Keys };
                        run = new Run(Curriculum.Generate(copy, "Повторение", random, 210), copy, "Повторение");
                        RenderText();
                    }
                    Start();
                };
                p.Children.Add(b);
            }
            var advanced = Card("Уже умеешь печатать?", "В разделе «Для опытных» сразу доступна полная раскладка: скорость, ритм, сложные слова, персональные пары, знаки и код. Начальный курс проходить не нужно.");
            var go = new Button { Content = "Перейти к тренировкам для опытных  →", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
            go.Click += delegate { Navigate("Expert"); }; advanced.Children.Add(go);
        }
        void Progress()
        {
            var sessions = profile.Sessions.Where(s => s.Language == profile.Language).ToList();
            var complete = sessions.Where(s => s.Completed).ToList();
            Card("У тебя уже " + complete.Count + " завершённых занятий", "Практика: " + (sessions.Sum(s => s.Seconds) / 60).ToString("0.0") + " мин  ·  " + profile.Xp + " XP на обеих раскладках  ·  " + profile.Streak + " дн. подряд. Незавершённые занятия сохраняют время и ошибки, но не дают XP.");
            var expertRuns = complete.Where(s => Expert.IsMode(s.Mode) && s.RequestedSeconds > 0 && s.Seconds >= s.RequestedSeconds - 1 && s.Accuracy >= 97).ToList();
            var expertSummary = expertRuns.AsEnumerable().Reverse().GroupBy(s => s.Mode + " / " + s.RequestedSeconds + " с").Take(6).Select(g => g.Key + "   ·   рекорд " + Math.Round(g.Max(s => s.Cpm)) + " зн/мин   ·   " + g.Count() + " зачётных занятий").ToArray();
            Card("Для опытных · личные рекорды", expertSummary.Length == 0 ? "Заверши тренировку в разделе «Для опытных» с точностью ≥97%. Рекорды разделяются по режиму, длительности и текущей раскладке." : string.Join("\n", expertSummary));
            var week = Card("Последние 7 дней · минуты практики", "Высота столбцов показывает активное время. День без занятия — просто свободный день.");
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 7, Height = 160, Margin = new Thickness(0, 20, 0, 0) };
            double max = Math.Max(profile.GoalMinutes, Enumerable.Range(0, 7).Max(i => profile.Sessions.Where(s => s.At.Date == DateTime.Today.AddDays(-i)).Sum(s => s.Seconds) / 60));
            for (int i = 6; i >= 0; i--)
            {
                DateTime day = DateTime.Today.AddDays(-i); double mins = profile.Sessions.Where(s => s.At.Date == day).Sum(s => s.Seconds) / 60;
                var col = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8, 0, 8, 0) };
                col.Children.Add(new TextBlock { Text = mins.ToString("0.0"), HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brush("#C4B4FA"), FontSize = 12, Margin = new Thickness(0, 0, 0, 6) });
                col.Children.Add(new Border { Height = Math.Max(3, 105 * mins / max), Background = Brush(i == 0 ? "#B4A0FF" : "#645683"), CornerRadius = new CornerRadius(5) });
                col.Children.Add(new TextBlock { Text = day.ToString("dd.MM"), HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brush("#999DB1"), FontSize = 11, Margin = new Thickness(0, 8, 0, 0) }); grid.Children.Add(col);
            }
            week.Children.Add(grid);
            var weak = profile.Keys.Where(k => k.Language == profile.Language && k.Key != " " && k.Key.Length == 1 && k.Attempts >= 3).OrderBy(k => k.Accuracy).ThenByDescending(k => k.Latency).Take(8).ToList();
            Card("Точки роста", weak.Count == 0 ? "После нескольких занятий здесь появятся буквы с ошибками и задержками." : string.Join("\n", weak.Select(k => k.Key.ToUpperInvariant() + "   ·   " + k.Accuracy.ToString("0") + "%   ·   " + k.Attempts + " нажатий   ·   " + (k.TimingSamples == 0 ? "мало данных по времени" : k.Latency.ToString("0") + " мс между нажатиями") + "   ·   повтор " + (k.Due <= DateTime.Now ? "сегодня" : k.Due.ToString("dd.MM"))).ToArray()));
            var badges = new List<string>();
            if (complete.Count > 0) badges.Add("✓ Первый шаг — завершено первое занятие");
            if (complete.Any(s => s.Accuracy >= 97 && s.Correct >= 80)) badges.Add("✓ Чистый ритм — ≥97% на 80+ знаках");
            if (profile.Streak >= 3) badges.Add("✓ Возвращение — три дня подряд");
            if (profile.TodaySeconds >= profile.GoalMinutes * 60) badges.Add("✓ Сегодня получилось — дневная цель выполнена");
            if (expertRuns.Any(s => s.TargetCpm > 0 && s.Cpm >= s.TargetCpm)) badges.Add("✓ Личный рубеж — цель скорости при точности ≥97%");
            Card("Твои достижения", badges.Count == 0 ? "Заверши первое занятие, чтобы открыть «Первый шаг»." : string.Join("\n", badges.ToArray()));
            var hist = Card("История · последние 20 занятий", "Зн/мин = верные нажатия / активные минуты. WPM = зн/мин / 5. Точность учитывает все ошибки, даже исправленные. Курс и тест сравнивай отдельно.");
            foreach (Session s in sessions.AsEnumerable().Reverse().Take(20)) hist.Children.Add(new TextBlock { Text = s.At.ToString("dd.MM HH:mm") + "   ·   " + s.Mode + "   ·   " + Math.Round(s.Cpm) + " зн/мин   ·   " + s.Accuracy.ToString("0.0") + "%   ·   " + s.Seconds.ToString("0") + " с   ·   " + (s.Completed ? "+" + s.Xp + " XP" : "прервано") + (Expert.IsMode(s.Mode) && s.RhythmSamples >= 10 ? "   ·   разброс " + s.RhythmCv.ToString("0") + "%" : ""), Margin = new Thickness(0, 12, 0, 0), Foreground = Brush("#B9BDD0") });
        }
        void Science()
        {
            Card("Практика, которая учит, а не просто измеряет", "Поставь пальцы на домашний ряд. Найди выступы на А/О или F/J, смотри на экран и возвращай палец в исходную позицию. Сохраняй устойчивое соответствие клавиш и пальцев. Для заглавной буквы удерживай Shift другой рукой. При ошибке курсор ждёт верную букву: Backspace не нужен.");
            var p = Card("01 / Регулярно, с промежутками", "Baddeley и Longman (1978) сравнили расписания обучения печати: распределённая практика была эффективнее интенсивной. Исследование сравнивало часовые блоки, поэтому не доказывает оптимальность 10 минут. Дневная цель 10 минут здесь — удобная отправная точка, которую можно изменить.");
            Link(p, "Baddeley & Longman, 1978 · исследование", "https://doi.org/10.1080/00140137808931764");
            p = Card("02 / Целенаправленно, с обратной связью", "Keith и Ericsson (2007), 60 участников: более высокий навык связан с обучением и намеренной практикой. Это не рандомизированное доказательство ускорения. SpeedKlava собирает ошибки и задержки и повышает вероятность слабых букв в следующих упражнениях.");
            Link(p, "Keith & Ericsson, 2007 · аннотация", "https://pubmed.ncbi.nlm.nih.gov/17924799/");
            p = Card("03 / Устойчивые движения и сочетания", "Feit, Weir и Oulasvirta (2016) наблюдали связь высокой скорости с постоянным пальцем для каждой клавиши, подготовкой следующих движений и небольшим перемещением кистей. Dhakal и коллеги (2018) исследовали 136 млн нажатий: сочетания букв и координация пальцев связаны со скоростью. Поэтому есть подсказки пальцев и тренировка пар. Корреляция не гарантирует результат конкретного тренажёра.");
            Link(p, "How We Type · CHI 2016", "https://userinterfaces.aalto.fi/how-we-type/");
            Link(p, "136 Million Keystrokes · CHI 2018", "https://userinterfaces.aalto.fi/136Mkeystrokes/");
            p = Card("04 / Новые данные и границы выводов", "Метаанализ 2025 года о самостоятельном выборе обратной связи в моторном обучении (29 исследований, 1147 участников) обнаружил преимущества для удержания и переноса, но не общее преимущество во время освоения. Здесь можно скрыть текущие метрики и клавиатуру; перенос этих выводов на печать остаётся проектной гипотезой. Публикации 2026 года о печати также не устанавливают универсально лучший алгоритм обучения.");
            Link(p, "Self-Controlled Feedback · 2025", "https://pmc.ncbi.nlm.nih.gov/articles/PMC12467369/");
            Link(p, "Исследование печати и усвоения текста · 2026", "https://www.scitepress.org/PublishedPapers/2026/147208/");
            Card("Что является решением программы", "Порог 97%, два зачётных занятия, интервалы 1/2/4/7/14 дней, веса адаптации, XP и значки выбраны для этой версии и не прошли экспериментальную валидацию. В адаптивном режиме около части материала повторяется чаще, остальная часть сохраняет разнообразие. Никаких гарантий скорости за определённое число дней. Источники проверены 07.11.2026.");
            Card("План одного занятия", "1. Вспомни исходную позицию и начни с курса.\n2. После двух-трёх повторов попробуй слабые клавиши или сочетания.\n3. На следующих днях возвращайся к практике. Иногда проходи тест на полном тексте.\n4. Сравнивай одинаковые раскладки и длительности. Если устал — остановись и вернись позже.");
            Card("Если ты уже печатаешь вслепую", "Открой «Для опытных». Начни с 60 секунд скорости, затем выбери слабые пары или сложные слова. Для устойчивого темпа используй режим «Ритм»: слова одинаковой длины позволяют уменьшить вариацию сложности. Разброс — коэффициент вариации интервалов между последовательными верными нажатиями (20–3000 мс). Ошибки и начало после паузы разрывают последовательность; показатель появляется после 10 интервалов. Это описательная метрика, не научный норматив и не оценка удержания навыка. Темп цели, шесть режимов и порог зачёта — настройки программы, не проверенная программа подготовки профессионалов.");
        }
        void Settings()
        {
            var p = Card("Твой комфортный темп", "Программа работает без аккаунта и интернета. Раскладка внутри программы не меняет системную раскладку Windows: переключай её Win + Пробел.");
            p.Children.Add(new TextBlock { Text = "Дневная цель", Margin = new Thickness(0, 20, 0, 8) });
            var goal = new ComboBox { Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (int n in new[] { 5, 10, 15, 20, 30 }) goal.Items.Add(n + " минут");
            int[] goals = { 5, 10, 15, 20, 30 }; goal.SelectedIndex = Math.Max(0, Array.IndexOf(goals, profile.GoalMinutes));
            goal.SelectionChanged += delegate { profile.GoalMinutes = goals[goal.SelectedIndex]; Save(); Refresh(); }; p.Children.Add(goal);
            Check(p, "Показывать клавиатуру и подсказку пальца", profile.Hints, v => { profile.Hints = v; RenderKeyboard(); RenderText(); });
            Check(p, "Показывать скорость и точность во время занятия", profile.LiveStats, v => profile.LiveStats = v);
            Check(p, "Увеличенный текст упражнения", profile.LargeText, v => { profile.LargeText = v; RenderText(); });
            p = Card("Прогресс под твоим контролем", "Данные хранятся локально в %LOCALAPPDATA%\\SpeedKlava\\progress.xml. При записи сохраняется предыдущая версия .bak. Экспортируй прогресс перед переносом на другой компьютер.");
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 15, 0, 0) };
            var export = new Button { Content = "Экспорт прогресса", Margin = new Thickness(0, 0, 12, 0) };
            export.Click += delegate { var d = new SaveFileDialog { Filter = "Прогресс SpeedKlava (*.xml)|*.xml", FileName = "SpeedKlava-progress.xml" }; if (d.ShowDialog() == true) try { Store.Write(profile, d.FileName); Notice("Прогресс экспортирован."); } catch (Exception e) { Notice(e.Message); } };
            var import = new Button { Content = "Импорт прогресса" };
            import.Click += delegate {
                var d = new OpenFileDialog { Filter = "Прогресс SpeedKlava (*.xml)|*.xml" };
                if (d.ShowDialog() != true) return;
                try {
                    Profile loaded = Store.Read(d.FileName);
                    if (MessageBox.Show("Заменить текущий прогресс импортированным? Перед заменой можно отменить и сделать экспорт.", "Импорт прогресса", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    EndPartial(); profile = loaded; initializing = true; Get<ComboBox>("LanguageBox").SelectedIndex = profile.Language == "RU" ? 0 : 1; UpdateGoalSelection(); if (expert && Mode == Expert.Code && profile.Language == "RU") Get<ComboBox>("ModeBox").SelectedIndex = 0; initializing = false; Save(); Refresh(); Prepare(); RenderOther(); Notice("Прогресс импортирован.");
                } catch (Exception e) { Notice("Импорт не выполнен: " + e.Message); }
            };
            actions.Children.Add(export); actions.Children.Add(import); p.Children.Add(actions);
            Card("О приложении", "SpeedKlava 1.1 · нативное приложение Windows на WPF / .NET Framework 4.8. Без рекламы, аналитики, глобального перехвата клавиш и сетевых запросов. Ввод обрабатывается только в активном упражнении. В текстах нет заимствованного исследовательского датасета.");
        }
        void Check(StackPanel parent, string label, bool value, Action<bool> change)
        {
            var c = new CheckBox { Content = label, IsChecked = value };
            c.Click += delegate { change(c.IsChecked == true); Save(); }; parent.Children.Add(c);
        }
        void Smoke()
        {
            try
            {
                Directory.CreateDirectory(smokeDirectory);
                // Exercise the same input path used by WPF without touching the user's profile.
                run = new Run("ао ао", profile, "Курс"); Start();
                Accept('x'); Ensure(run.Attempts == 0, "wrong-layout guard");
                Accept('о'); Ensure(run.Errors == 1 && run.Position == 0, "error feedback");
                Accept('а'); Ensure(run.Position == 1, "advance cursor");
                TogglePause(); Accept('о'); Ensure(run.Position == 1, "pause blocks input");
                TogglePause(); Accept('о'); Accept(' '); Ensure(run.Position == 3, "resume and space");
                EndPartial(); Ensure(profile.Sessions.Count == 1 && profile.Sessions[0].Xp == 0, "partial save");
                Get<ComboBox>("LanguageBox").SelectedIndex = 1;
                Ensure(profile.Language == "EN" && run.Language == "EN", "language switch");
                Get<ComboBox>("ModeBox").SelectedIndex = 4;
                Ensure(Get<StackPanel>("KeyboardPanel").Visibility == Visibility.Collapsed, "test hides keyboard");
                Start(); Accept(run.Text[0]); run.ActiveSeconds = limit; Tick(null, EventArgs.Empty);
                Ensure(committed && Get<Border>("ResultPanel").Visibility == Visibility.Visible, "timed completion result");
                Navigate("Expert");
                Ensure(expert && Mode == Expert.Speed && run.Text.Length >= 2500 && !ShowHints && profile.Stage == 0, "expert immediately available on full layout");
                Start(); Accept(run.Text[0]); TogglePause(); TogglePause(); Tick(null, EventArgs.Empty);
                Ensure(!paused, "resume does not immediately idle-pause"); EndPartial();
                for (int m = 0; m < Expert.Modes.Length; m++)
                {
                    Get<ComboBox>("ModeBox").SelectedIndex = m;
                    Start(); Accept(run.Text[0]); Ensure(run.Position == 1 && Expert.IsMode(run.Mode), "expert input " + m); EndPartial();
                }
                Ensure(profile.Language == "EN" && Mode == Expert.Code, "code switches to EN");
                Get<ComboBox>("DurationBox").SelectedIndex = 3;
                Get<ComboBox>("ExpertGoalBox").SelectedIndex = 5;
                Ensure(run.RequestedSeconds == 300 && run.TargetCpm == 400 && run.Text.Length >= 9000, "expert duration and goal");
                Get<ComboBox>("LanguageBox").SelectedIndex = 0;
                Ensure(Mode == Expert.Speed && profile.Stage == 0, "switching RU leaves EN-only code without affecting course");
                Navigate("Train");
                Ensure(!expert && Mode == "Курс" && Get<Border>("ExpertPanel").Visibility == Visibility.Collapsed, "return to beginner course");
                Get<ComboBox>("DurationBox").SelectedIndex = 1;
                Get<ComboBox>("LanguageBox").SelectedIndex = 0;
                Get<ComboBox>("ModeBox").SelectedIndex = 0;
                profile = new Profile(); initializing = true; UpdateGoalSelection(); initializing = false; Refresh(); Prepare();
                string[] pages = { "Train", "Expert", "Lessons", "Progress", "Science", "Settings", "Small", "ExpertSmall", "Done" }; int index = 0;
                var timerShot = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                timerShot.Tick += delegate {
                    try
                    {
                        string current = pages[index++];
                        if (current == "Done") { timerShot.Stop(); File.WriteAllText(Path.Combine(smokeDirectory, "smoke-result.txt"), "PASS: beginner regression; all six expert modes; EN code; target; five-minute duration; full layout at stage zero; pause/resume; mode/language transitions; all six pages; minimum-size layouts; eight screenshots."); window.Close(); return; }
                        if (current == "Small") { Navigate("Train"); window.Width = 1000; window.Height = 720; }
                        else if (current == "ExpertSmall") Navigate("Expert");
                        else Navigate(current);
                        window.UpdateLayout();
                        var content = (FrameworkElement)window.Content;
                        var bmp = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32); bmp.Render(content);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bmp));
                        using (var file = File.Create(Path.Combine(smokeDirectory, current == "Train" ? "SpeedKlava.png" : current + ".png"))) encoder.Save(file);
                    }
                    catch (Exception e) { timerShot.Stop(); File.WriteAllText(Path.Combine(smokeDirectory, "smoke-error.txt"), e.ToString()); Environment.ExitCode = 1; window.Close(); }
                }; timerShot.Start();
            }
            catch (Exception e) { File.WriteAllText(Path.Combine(smokeDirectory, "smoke-error.txt"), e.ToString()); window.Close(); Environment.ExitCode = 1; }
        }
        static void Ensure(bool ok, string check) { if (!ok) throw new Exception("GUI smoke failed: " + check); }
    }
}
