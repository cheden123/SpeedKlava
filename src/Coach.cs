using System;
using System.Linq;
using System.Collections.Generic;

namespace SpeedKlava
{
    public class Reward
    {
        public string Id = "";
        public string Title = "";
        public DateTime At = DateTime.Now;
        public int Xp;
    }
    public class Mission
    {
        public string Id;
        public string Title;
        public string Detail;
        public string Mode;
        public int Xp;
        public int Value;
        public int Goal;
        public bool Done { get { return Value >= Goal; } }
    }
    public class Achievement
    {
        public string Id;
        public string Title;
        public string Detail;
        public int Xp;
        public double Value;
        public double Goal;
        public bool Done { get { return Value >= Goal; } }
    }
    public static class Coach
    {
        public static List<Mission> Missions(Profile p, DateTime day, bool expert)
        {
            var runs = p.Sessions.Where(s => s.At.Date == day.Date && s.Completed && s.Seconds >= 20 && s.Correct >= 20).ToList();
            return new List<Mission> {
                new Mission { Id = "practice", Title = "Вернуться к клавишам", Detail = "Заверши два занятия: от 20 секунд и 20 верных знаков каждое.", Xp = 30, Value = Math.Min(2, runs.Count), Goal = 2, Mode = expert ? Expert.Speed : "Курс" },
                new Mission { Id = "accuracy", Title = "Точность важнее спешки", Detail = "Заверши занятие: ≥97%, от 80 верных знаков и 20 секунд.", Xp = 25, Value = runs.Any(s => s.Accuracy >= 97 && s.Correct >= 80) ? 1 : 0, Goal = 1, Mode = expert ? Expert.Rhythm : "Курс" },
                new Mission { Id = "focus", Title = "Один трудный участок", Detail = "Заверши слабые клавиши, сочетания, слабые пары или сложные слова: от 20 секунд и 20 верных знаков.", Xp = 25, Value = runs.Any(s => s.Mode == "Слабые клавиши" || s.Mode == "Сочетания" || s.Mode == Expert.Weak || s.Mode == Expert.Hard) ? 1 : 0, Goal = 1, Mode = expert ? Expert.Weak : "Слабые клавиши" }
            };
        }
        public static List<Achievement> Achievements(Profile p)
        {
            var runs = p.Sessions.Where(s => s.Completed && s.Seconds >= 20 && s.Correct >= 20).ToList();
            int chars = runs.Sum(s => s.Correct);
            int days = runs.Select(s => s.At.Date).Distinct().Count();
            return new List<Achievement> {
                new Achievement { Id = "first", Title = "Первый уверенный шаг", Detail = "Одно завершённое занятие от 20 секунд и 20 знаков", Xp = 30, Value = runs.Count, Goal = 1 },
                new Achievement { Id = "accurate", Title = "Чистый ритм", Detail = "Занятие с ≥97% точности и от 80 верных знаков", Xp = 40, Value = runs.Count(s => s.Accuracy >= 97 && s.Correct >= 80), Goal = 1 },
                new Achievement { Id = "thousand", Title = "Тысяча движений", Detail = "1000 верных знаков в завершённых занятиях", Xp = 60, Value = chars, Goal = 1000 },
                new Achievement { Id = "five-thousand", Title = "Клавиши становятся знакомыми", Detail = "5000 верных знаков в завершённых занятиях", Xp = 100, Value = chars, Goal = 5000 },
                new Achievement { Id = "ten-thousand", Title = "Десять тысяч шагов", Detail = "10000 верных знаков в завершённых занятиях", Xp = 150, Value = chars, Goal = 10000 },
                new Achievement { Id = "ten-runs", Title = "Практика вошла в привычку", Detail = "10 завершённых занятий от 20 секунд и 20 знаков", Xp = 75, Value = runs.Count, Goal = 10 },
                new Achievement { Id = "three-days", Title = "Возвращение", Detail = "Практика в три разных дня; подряд не обязательно", Xp = 60, Value = days, Goal = 3 },
                new Achievement { Id = "seven-days", Title = "На своей стороне", Detail = "Практика в семь разных дней; пропуски допустимы", Xp = 100, Value = days, Goal = 7 },
                new Achievement { Id = "half-hour", Title = "Время для навыка", Detail = "30 минут завершённой практики", Xp = 75, Value = runs.Sum(s => s.Seconds) / 60, Goal = 30 },
                new Achievement { Id = "expert-goal", Title = "Личный рубеж", Detail = "Полный профи-режим: цель скорости и ≥97% точности", Xp = 80, Value = runs.Count(s => Expert.IsMode(s.Mode) && s.TargetCpm > 0 && s.RequestedSeconds > 0 && s.Seconds >= s.RequestedSeconds - 1 && s.Accuracy >= 97 && s.Cpm >= s.TargetCpm), Goal = 1 }
            };
        }
        public static List<Reward> Award(Profile p, Session current)
        {
            var added = new List<Reward>();
            if (!current.Completed || current.Seconds < 20 || current.Correct < 20) return added;
            foreach (Mission m in Missions(p, current.At.Date, Expert.IsMode(current.Mode)))
                if (m.Done) Add(p, added, "day:" + current.At.ToString("yyyy-MM-dd") + ":" + m.Id, m.Title, m.Xp, current.At);
            foreach (Achievement a in Achievements(p))
                if (a.Done) Add(p, added, "badge:" + a.Id, a.Title, a.Xp, current.At);
            current.BonusXp += added.Sum(r => r.Xp);
            return added;
        }
        static void Add(Profile p, List<Reward> added, string id, string title, int xp, DateTime at)
        {
            if (p.Rewards.Any(r => r.Id == id)) return;
            var reward = new Reward { Id = id, Title = title, Xp = xp, At = at }; p.Rewards.Add(reward); added.Add(reward);
        }
        public static string Encouragement(Profile p)
        {
            int today = p.Sessions.Count(s => s.At.Date == DateTime.Today && s.Completed && s.Seconds >= 20 && s.Correct >= 20);
            if (p.TodaySeconds >= p.GoalMinutes * 60) return "Цель дня выполнена. Можно отдохнуть и вернуться завтра.";
            if (today >= 2) return "Два занятия уже позади. Сделай паузу и отметь свой шаг.";
            if (today == 1) return "Первое занятие готово. Продолжи после небольшой паузы.";
            if (p.Sessions.Any(s => s.Completed)) return "Продолжай с прежнего места. Пропуск не обнуляет опыт.";
            return "Начни с 30 секунд. Первый шаг не обязан быть идеальным.";
        }
        public static string Feedback(Profile p, Session current, Run run)
        {
            if (!current.Completed) return current.Correct + " верных знаков сохранено. Пауза не отменяет сделанного; продолжи, когда будет удобно.";
            string message = "За это занятие: " + current.Correct + " верных знаков. ";
            var before = p.Sessions.Where(s => !object.ReferenceEquals(s, current) && s.Completed && s.Language == current.Language && s.Mode == current.Mode && s.RequestedSeconds == current.RequestedSeconds && (Expert.IsMode(current.Mode) || s.Stage == current.Stage) && s.Seconds >= 20).Reverse().Take(3).ToList();
            if (before.Count > 0 && current.Seconds >= 20)
            {
                double previous = 100.0 * before.Sum(s => s.Correct) / Math.Max(1, before.Sum(s => s.Attempts));
                if (current.Accuracy >= previous + .5) message += "Точность выше предыдущих " + before.Count + " сопоставимых занятий на " + (current.Accuracy - previous).ToString("0.0") + " п.п. ";
            }
            var errors = run.Delta.Values.Where(k => k.Key.Length == 1 && k.Errors > 0).OrderByDescending(k => k.Errors).Take(3).ToList();
            if (errors.Count > 0) message += "Чаще мешали: " + string.Join(", ", errors.Select(k => (k.Key == " " ? "пробел" : k.Key) + " (" + k.Errors + ")").ToArray()) + ". ";
            if (current.Accuracy < 97) return message + "Следующий шаг: один спокойный подход на слабые места. Перед нажатием найди нужную букву взглядом в тексте; не ускоряйся после ошибки.";
            if (current.RhythmSamples >= 10 && current.RhythmCv > 60) return message + "Точность уже хорошая. Следующий шаг: короткое занятие на ровный ритм, без резкого ускорения на знакомых словах.";
            if (Expert.IsMode(current.Mode)) return message + "Точность ≥97% удержана. Повтори этот темп в другой день, прежде чем повышать цель.";
            return message + "Точность ≥97% удержана. Следующий шаг: закрепи текущие клавиши и попробуй читать на одно слово вперёд.";
        }
        public static string LessonTitle(Profile p, string mode)
        {
            return Expert.IsMode(mode) ? mode.Replace("Профи · ", "") + ": на что обратить внимание" : Curriculum.Names[p.Stage];
        }
        public static string Lesson(Profile p, int stage)
        {
            string home = p.Language == "RU" ? "ФЫВА · ОЛДЖ" : "ASDF · JKL;";
            string bumps = p.Language == "RU" ? "А и О" : "F и J";
            string[] steps = {
                "Найди выступы на " + bumps + ". Поставь на них указательные пальцы, остальные держи над " + home + ".\nПопробуй: посмотри на экран и чередуй две опорные клавиши. Сначала удобное движение, затем темп.\nЕсли промахнулся: верни пальцы к выступам, а не ищи всю клавиатуру взглядом.",
                "Добавляем соседние пальцы к опорным клавишам. Не переноси всю кисть вслед за одной буквой.\nПопробуй: назови палец для подсвеченной клавиши, затем нажми её, глядя на экран.\nЧастая ошибка: пытаться нажать все новые буквы указательными. Подсказка под текстом помогает закрепить постоянный палец.",
                "Домашняя позиция: " + home + ". До соседних клавиш дотягивается палец, а кисть остаётся близко к исходному месту.\nПопробуй: набери короткую группу, верни пальцы домой и продолжи следующую.\nЧастая ошибка: крепко прижимать пальцы и зажимать кисти. Расслабь руки перед новым подходом.",
                "Верхний ряд — движение от домашней позиции вверх и обратно. Сохраняй ту же зону каждого пальца.\nПопробуй: перед новой буквой мысленно свяжи её с пальцем и ближайшей домашней клавишей.\nЕсли буквы путаются: выбери слабые клавиши и снизь темп на один подход.",
                "Нижний ряд требует короткого движения пальца вниз. Не уводи обе руки к краю клавиатуры.\nПопробуй: медленно чередуй букву нижнего ряда с уже знакомой буквой.\nЧастая ошибка: торопиться после пробела. Начинай следующее слово в том же спокойном темпе.",
                p.Language == "RU" ? "Закрепляем полную раскладку, включая Ё и Ъ. Ё — слева от 1; Ъ — справа от Х.\nПопробуй: произнеси трудную букву мысленно и нажми её без взгляда вниз.\nЕсли редкая буква тормозит: потренируй её в сочетании, а затем в слове." : "Закрепляем все буквы. Проверь, что каждая буква связана с постоянным пальцем.\nПопробуй: читай следующее короткое слово, пока заканчиваешь текущее.\nЕсли сбиваешься: верни взгляд к текущей букве и восстанови точность.",
                "Числа и знаки тоже входят в навык. Для заглавной буквы и Shift-знака используй Shift другой рукой.\nПопробуй: прочитай число небольшой группой и набери её без спешки.\nВажно: положение знаков зависит от системной раскладки Windows. Проверь RU/EN перед занятием.",
                "Теперь объединяем движения в связный текст. Смотри на текст, а не на пальцы, и читай немного вперёд.\nПопробуй: один подход на точность, затем — другой текст или сочетания, которые мешали.\nСравнивай одинаковые режимы и длительности. Если устал, сделай паузу; не пытайся любой ценой побить рекорд."
            };
            return steps[Math.Max(0, Math.Min(steps.Length - 1, stage))];
        }
        public static string Tip(Profile p, string mode)
        {
            if (!Expert.IsMode(mode)) return Lesson(p, p.Stage);
            if (mode == Expert.Code) return "Сначала прочитай весь фрагмент: где скобки, кавычки и заглавные буквы?\nКод набирается в одну строку. Пробелы и регистр значимы; Tab и Enter здесь не нужны.\nНачни с 30 секунд точного ввода. Если тормозит знак, задержись перед ним, сохрани точность и продолжи.";
            if (mode == Expert.Rhythm) return "Не разгоняйся резко на простом слове и не замирай перед следующим.\nСлова одинаковой длины помогают наблюдать собственный темп. Разброс — описание интервалов, а не оценка твоих способностей.\nПопробуй 30 секунд с удобной целью. Сравни ритм только с этим же режимом.";
            if (mode == Expert.Weak) return "Пара выбирается по твоим ошибкам и задержкам; пока данных мало — используются частые пары.\nПрочитай пару как одно движение. Затем набери слово с ней, не ускоряясь на остальной части слова.\nОдин спокойный повтор полезнее для наблюдения, чем серия попыток на пределе скорости.";
            if (mode == Expert.Hard) return "Раздели длинное слово на знакомые группы букв. Читай ближайшую группу, а не пытайся удержать всё слово сразу.\nПосле ошибки верни удобный темп. Не компенсируй задержку резким ускорением.\nСравнивай результаты сложных слов отдельно от частотных слов.";
            if (mode == Expert.Symbols) return "Сначала замечай регистр и ближайший знак. Для Shift используй другую руку.\nДаты и числа читай небольшими группами. Время, кавычки и пунктуация требуют отдельного внимания.\nПосле занятия проверь, какие именно знаки дали ошибки, и повтори короткий подход.";
            return "Выбери темп, при котором получается удерживать ≥97% точности. Цель — ориентир, а не обязанность.\nЧитай на слово вперёд; пробел завершает одно слово и начинает следующее движение.\nПеред повышением цели повтори результат в другой день. Не сравнивай этот режим со сложными словами или кодом.";
        }
    }
}
