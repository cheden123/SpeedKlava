using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Xml.Serialization;

namespace SpeedKlava
{
    public class KeyStat
    {
        public string Key = "";
        public string Language = "RU";
        public int Attempts;
        public int Errors;
        public int TimingSamples;
        public double Milliseconds;
        public int Box;
        public DateTime Due = DateTime.MinValue;
        public double Accuracy { get { return Attempts == 0 ? 0 : 100.0 * (Attempts - Errors) / Attempts; } }
        public double Latency { get { return TimingSamples == 0 ? 0 : Milliseconds / TimingSamples; } }
    }
    public class Session
    {
        public DateTime At = DateTime.Now;
        public string Language = "RU";
        public string Mode = "Курс";
        public double Seconds;
        public int Correct;
        public int Attempts;
        public int Errors;
        public int Stage;
        public int Xp;
        public bool Completed;
        public double Accuracy { get { return Attempts == 0 ? 0 : Correct * 100.0 / Attempts; } }
        public double Cpm { get { return Seconds <= 0 ? 0 : Correct * 60.0 / Seconds; } }
        public double Wpm { get { return Cpm / 5; } }
    }
    public class Profile
    {
        public int Version = 1;
        public int RuStage;
        public int EnStage;
        public string Language = "RU";
        public int GoalMinutes = 10;
        public bool Hints = true;
        public bool LiveStats = true;
        public bool LargeText;
        public List<KeyStat> Keys = new List<KeyStat>();
        public List<Session> Sessions = new List<Session>();
        public int Stage { get { return Language == "RU" ? RuStage : EnStage; } set { if (Language == "RU") RuStage = value; else EnStage = value; } }
        public int Xp { get { return Sessions.Sum(s => s.Xp); } }
        public KeyStat Stat(string key, string language)
        {
            KeyStat stat = Keys.FirstOrDefault(k => k.Key == key && k.Language == language);
            if (stat == null) { stat = new KeyStat { Key = key, Language = language }; Keys.Add(stat); }
            return stat;
        }
        public double TodaySeconds { get { return Sessions.Where(s => s.At.Date == DateTime.Today).Sum(s => s.Seconds); } }
        public int Streak
        {
            get
            {
                var days = new HashSet<DateTime>(Sessions.Where(s => s.Completed).Select(s => s.At.Date));
                DateTime day = days.Contains(DateTime.Today) ? DateTime.Today : DateTime.Today.AddDays(-1);
                int count = 0; while (days.Contains(day)) { count++; day = day.AddDays(-1); } return count;
            }
        }
    }
    public static class Store
    {
        public static string Folder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpeedKlava"); } }
        public static string FilePath { get { return Path.Combine(Folder, "progress.xml"); } }
        public static string Warning = "";
        public static Profile Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new Profile();
                return Read(FilePath);
            }
            catch (Exception)
            {
                Warning = "Не удалось прочитать прогресс. Пробуем резервную копию; экспортируй восстановленные данные в настройках.";
                try { return Read(FilePath + ".bak"); } catch { return new Profile(); }
            }
        }
        public static Profile Read(string path)
        {
            var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 20000000 };
            using (var reader = System.Xml.XmlReader.Create(path, settings))
            {
                Profile p = (Profile)new XmlSerializer(typeof(Profile)).Deserialize(reader);
                if (p == null || p.Keys == null || p.Sessions == null || p.Version != 1) throw new InvalidDataException("Неизвестный формат прогресса.");
                p.Language = p.Language == "EN" ? "EN" : "RU";
                p.RuStage = Math.Max(0, Math.Min(Curriculum.Count - 1, p.RuStage));
                p.EnStage = Math.Max(0, Math.Min(Curriculum.Count - 1, p.EnStage));
                p.GoalMinutes = Math.Max(5, Math.Min(30, p.GoalMinutes));
                if (p.Keys.Any(k => k == null || k.Key == null || k.Attempts < 0 || k.Errors < 0 || k.Errors > k.Attempts) || p.Sessions.Any(s => s == null || s.Seconds < 0 || double.IsNaN(s.Seconds) || double.IsInfinity(s.Seconds) || s.Correct < 0 || s.Attempts < s.Correct || s.Xp < 0)) throw new InvalidDataException("Повреждённые данные прогресса.");
                return p;
            }
        }
        public static void Write(Profile profile, string path)
        {
            using (var writer = new StreamWriter(path, false, new System.Text.UTF8Encoding(false))) new XmlSerializer(typeof(Profile)).Serialize(writer, profile);
        }
        public static void Save(Profile profile)
        {
            Directory.CreateDirectory(Folder);
            SaveTo(profile, FilePath);
        }
        public static void SaveTo(Profile profile, string path)
        {
            string temp = path + ".tmp";
            Write(profile, temp);
            if (!File.Exists(path)) { File.Move(temp, path); return; }
            try { File.Replace(temp, path, path + ".bak", true); }
            catch (IOException)
            {
                // Some managed/virtual Windows filesystems do not implement ReplaceFile.
                // Keep a complete backup before the recoverable, non-atomic fallback.
                File.Copy(path, path + ".bak", true);
                File.Copy(temp, path, true);
                File.Delete(temp);
            }
        }
    }
    public static class Curriculum
    {
        public const int Count = 8;
        public static readonly string[] Names = { "Опора: два указательных", "Домашний ряд", "Весь средний ряд", "Верхний ряд", "Нижний ряд", "Редкие буквы", "Числа и знаки", "Живой текст" };
        public static readonly string[] Ru = { "ао", "аовы", "фывапролджэ", "фывапролджэйцукенгшщзхъ", "фывапролджэйцукенгшщзхъячсмитьбю", "фывапролджэйцукенгшщзхъячсмитьбюё", "фывапролджэйцукенгшщзхъячсмитьбюё0123456789.,!?-", "фывапролджэйцукенгшщзхъячсмитьбюё0123456789.,!?-" };
        public static readonly string[] En = { "fj", "fjdk", "asdfghjkl;", "asdfghjkl;qwertyuiop", "asdfghjkl;qwertyuiopzxcvbnm", "asdfghjkl;qwertyuiopzxcvbnm", "abcdefghijklmnopqrstuvwxyz0123456789.,!?-", "abcdefghijklmnopqrstuvwxyz0123456789.,!?-" };
        public static readonly string[] RuWords = ("дом мир пора вода дрова волк лодка радар плов двор флаг выдра повар дорога город работа слово клавиша письмо ритм рука пальцы строка экран время навык практика точность скорость память задача проект код функция массив данные окно кнопка ответ вопрос поезд берег солнце ветер облако трава дерево птица книга музыка свет новый тихий быстрый ровный простой каждый сегодня завтра постепенно уверенно спокойно легко лучше учиться печатать смотреть пробовать создавать повторять развивать замечать возвращаться сложный сочетание движение чувство свобода ёлка ещё объём подъезд жизнь щука чай чашка цифра юг язык помощь" ).Split(' ');
        public static readonly string[] EnWords = ("a as add all ask dad fall flag glass half sad salad ask flask dark far lake walk word work world type time key hand finger screen rhythm accuracy speed learn practice focus calm flow light window button code data array class function return value build design project small steady every today tomorrow better new bright write read create repeat space progress simple sentence quiet river tree cloud music book story quick brown fox jumps over lazy dog" ).Split(' ');
        public static readonly string[] RuTexts = {
            "Сегодня я учусь печатать спокойно и точно. Каждое движение становится увереннее, а взгляд остаётся на экране.",
            "За окном шумит ветер. На столе лежит книга, рядом остывает чай. Можно сделать паузу и продолжить с новыми силами.",
            "Хорошая программа начинается с понятной задачи. Раздели большой проект на небольшие шаги и проверь результат каждого из них.",
            "Утром город просыпается постепенно: открываются окна, загорается свет и первые поезда отправляются в путь.",
            "Новый навык требует времени. Даже небольшое занятие помогает заметить ошибки, найти удобный ритм и двигаться дальше."
        };
        public static readonly string[] EnTexts = {
            "A quiet morning is a good time to learn. Keep your eyes on the screen and let your fingers find a steady rhythm.",
            "The next project starts with a simple idea. Write a small function, check the result, and make one useful change at a time.",
            "Beyond the window, clouds drift over the river. A small bird lands on a branch while the city slowly wakes up.",
            "Learning takes time and attention. Notice what feels difficult, practice with care, and return tomorrow for another step.",
            "Every book opens a new world. Some stories take us far away, while others help us see familiar places in a different light."
        };
        public static string Allowed(Profile p) { return (p.Language == "RU" ? Ru : En)[p.Stage]; }
        public static string Generate(Profile p, string mode, Random random, int length)
        {
            string[] texts = p.Language == "RU" ? RuTexts : EnTexts;
            if (mode == "Тест" || mode == "Тексты" || (mode == "Курс" && p.Stage == Count - 1))
            {
                return string.Join(" ", texts.OrderBy(x => random.Next()).ToArray());
            }
            string allowed = Allowed(p);
            List<KeyStat> weak = p.Keys.Where(k => k.Language == p.Language && k.Key.Length == 1 && allowed.Contains(k.Key) && k.Attempts >= 3).OrderByDescending(k => (100 - k.Accuracy) * 4 + k.Latency / 20 + (k.Due <= DateTime.Now ? 15 : 0)).Take(4).ToList();
            string[] words = (p.Language == "RU" ? RuWords : EnWords).Where(w => w.All(c => allowed.Contains(c))).ToArray();
            var result = new List<string>(); int size = 0;
            while (size <= length)
            {
                string word;
                if (mode == "Сочетания")
                {
                    var pairs = p.Keys.Where(k => k.Language == p.Language && k.Key.Length == 2 && k.Key.All(c => allowed.Contains(c)) && k.Attempts >= 2).OrderByDescending(k => (100 - k.Accuracy) * 4 + k.Latency / 20).Take(8).ToArray();
                    word = pairs.Length > 0 ? pairs[random.Next(pairs.Length)].Key : "" + allowed[random.Next(allowed.Length)] + allowed[random.Next(allowed.Length)];
                }
                else if (words.Length >= 3 && random.NextDouble() < .75)
                {
                    string[] focused = words.Where(w => weak.Any(k => w.Contains(k.Key))).ToArray();
                    var pool = (mode == "Слабые клавиши" && focused.Length > 0) || (focused.Length > 0 && random.NextDouble() < .45) ? focused : words;
                    word = pool[random.Next(pool.Length)];
                }
                else
                {
                    word = ""; int n = random.Next(2, 5);
                    for (int j = 0; j < n; j++) word += weak.Count > 0 && random.NextDouble() < .55 ? weak[random.Next(weak.Count)].Key : allowed[random.Next(allowed.Length)].ToString();
                }
                if (p.Stage >= 6 && mode == "Курс" && random.NextDouble() < .22) word = random.Next(10, 999).ToString() + ".";
                result.Add(word); size += word.Length + 1;
            }
            return string.Join(" ", result.ToArray());
        }
        public static char PhysicalKey(char c, string language)
        {
            c = char.ToLowerInvariant(c);
            string shifted = language == "RU" ? "!\"№;%:?*()_," : "!@#$%^&*()_+{}:\"<>?";
            string normal = language == "RU" ? "1234567890-." : "1234567890-=[];',./";
            int index = shifted.IndexOf(c);
            return index >= 0 && index < normal.Length ? normal[index] : c;
        }
        public static string Finger(char c, string language)
        {
            c = PhysicalKey(c, language);
            string[] groups = language == "RU" ? new[] { "ё1йфя", "2цыч", "3увс", "45каепми", "67нрготь", "8шлб", "9щдю", "0-зжэъх=.,!?" } : new[] { "`1qaz", "2wsx", "3edc", "45rtfgvb", "67yuhjnm", "8ik,", "9ol.", "0-p;/'[]=!?" };
            string[] labels = { "Левый мизинец", "Левый безымянный", "Левый средний", "Левый указательный", "Правый указательный", "Правый средний", "Правый безымянный", "Правый мизинец" };
            if (c == ' ') return "Большой палец · пробел";
            for (int i = 0; i < groups.Length; i++) if (groups[i].Contains(c)) return labels[i];
            return "Смотри на текст, держи кисти спокойно";
        }
    }
    public class Run
    {
        public string Text;
        public int Position;
        public int Correct;
        public int Attempts;
        public int Errors;
        public double ActiveSeconds;
        public bool Finished;
        public bool LastError;
        public string Language;
        public string Mode;
        public int Stage;
        public double LastTime = -1;
        public Dictionary<string, KeyStat> Delta = new Dictionary<string, KeyStat>();
        public Run(string text, Profile p, string mode) { Text = text; Language = p.Language; Mode = mode; Stage = p.Stage; }
        public double Accuracy { get { return Attempts == 0 ? 100 : 100.0 * Correct / Attempts; } }
        public double Cpm { get { return ActiveSeconds < 1 ? 0 : Correct * 60.0 / ActiveSeconds; } }
        public bool Input(char c)
        {
            if (Finished || Position >= Text.Length) return false;
            bool good = Text[Position] == c;
            Attempts++; if (good) Correct++; else Errors++;
            string key = char.ToLowerInvariant(Text[Position]).ToString();
            Record(key, good);
            if (Position > 0 && Text[Position - 1] != ' ' && Text[Position] != ' ') Record(Text.Substring(Position - 1, 2).ToLowerInvariant(), good);
            LastTime = ActiveSeconds;
            LastError = !good;
            if (good) { Position++; Finished = Position == Text.Length; }
            return good;
        }
        void Record(string key, bool good)
        {
            if (!Delta.ContainsKey(key)) Delta[key] = new KeyStat { Key = key, Language = Language };
            KeyStat stat = Delta[key]; stat.Attempts++; if (!good) stat.Errors++;
            double ms = (ActiveSeconds - LastTime) * 1000;
            if (good && LastTime >= 0 && ms >= 20 && ms <= 3000) { stat.TimingSamples++; stat.Milliseconds += ms; }
        }
        public Session Commit(Profile p, bool completed)
        {
            var s = new Session { Language = Language, Mode = Mode, Seconds = Math.Round(ActiveSeconds, 2), Correct = Correct, Attempts = Attempts, Errors = Errors, Stage = Stage, Completed = completed };
            s.Xp = completed ? Math.Min(200, (int)(Correct / 4.0 * (Accuracy / 100)) + (Accuracy >= 97 ? 20 : 0)) : 0;
            p.Sessions.Add(s);
            foreach (KeyStat d in Delta.Values)
            {
                KeyStat k = p.Stat(d.Key, Language);
                k.Attempts += d.Attempts; k.Errors += d.Errors; k.TimingSamples += d.TimingSamples; k.Milliseconds += d.Milliseconds;
                if (d.Errors > 0) k.Box = 0; else k.Box = Math.Min(5, k.Box + 1);
                k.Due = DateTime.Now.AddDays(new[] { 0, 1, 2, 4, 7, 14 }[k.Box]);
            }
            if (completed && Mode == "Курс" && Accuracy >= 97 && Correct >= 80)
            {
                int passes = p.Sessions.AsEnumerable().Reverse().TakeWhile(x => x.Language == Language && x.Mode == "Курс" && x.Stage == Stage && x.Completed && x.Accuracy >= 97 && x.Correct >= 80).Count();
                if (passes >= 2) { if (Language == "RU") p.RuStage = Math.Min(Curriculum.Count - 1, Stage + 1); else p.EnStage = Math.Min(Curriculum.Count - 1, Stage + 1); }
            }
            return s;
        }
    }
}
