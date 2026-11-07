using System;
using System.Linq;
using System.IO;
using SpeedKlava;

class EngineTests
{
    static int assertions;
    static void Assert(bool ok, string label) { assertions++; if (!ok) throw new Exception("FAIL: " + label); }
    static Run Type(Profile p, string text, string mode, bool error)
    {
        var r = new Run(text, p, mode);
        if (error) { r.ActiveSeconds = .2; r.Input('#'); }
        foreach (char c in text) { r.ActiveSeconds += .3; r.Input(c); }
        return r;
    }
    static int Main()
    {
        try
        {
            var p = new Profile();
            var r = new Run("ао", p, "Курс");
            r.ActiveSeconds = .3; Assert(!r.Input('ы') && r.Position == 0, "error waits for correction");
            r.ActiveSeconds = .6; Assert(r.Input('а') && r.Position == 1, "correct input advances");
            r.ActiveSeconds = .9; r.Input('о');
            Assert(r.Finished && r.Correct == 2 && r.Errors == 1 && r.Attempts == 3, "attempt accounting");
            Assert(Math.Abs(r.Accuracy - 200.0 / 3) < .001, "correction does not erase errors");
            Assert(!r.Input('о') && r.Attempts == 3, "finished run rejects input");
            var s = r.Commit(p, true);
            Assert(Math.Abs(s.Cpm - 2 * 60 / .9) < .01 && Math.Abs(s.Wpm - s.Cpm / 5) < .01, "speed uses correct characters and five-character WPM");
            Assert(p.Stat("а", "RU").Errors == 1 && p.Stat("а", "RU").Box == 0, "error resets repetition interval");
            Assert(p.Stat("о", "RU").Due > DateTime.Now && p.Keys.Any(k => k.Key == "ао"), "successful review and pair recording");
            Assert(p.RuStage == 0, "short run cannot unlock stage");
            p = new Profile();
            Type(p, new string('а', 100), "Курс", false).Commit(p, true);
            Assert(p.RuStage == 0, "one pass insufficient");
            Type(p, new string('о', 100), "Курс", false).Commit(p, true);
            Assert(p.RuStage == 1, "two accurate substantial runs unlock stage");
            Assert(p.Streak == 1 && p.Xp > 0, "streak and reward");
            var partial = Type(p, new string('а', 100), "Курс", false).Commit(p, false);
            Assert(partial.Xp == 0 && !partial.Completed && p.RuStage == 1, "partial run no XP or unlock");
            p.Language = "EN";
            Assert(p.Stage == 0, "language stages isolated");
            Type(p, "fjfjfjfj", "Курс", false).Commit(p, true);
            Assert(p.Keys.Any(k => k.Language == "EN") && p.Keys.Any(k => k.Language == "RU"), "stats isolated by language");
            foreach (string lang in new[] { "RU", "EN" }) for (int stage = 0; stage < Curriculum.Count - 1; stage++)
            {
                var pr = new Profile { Language = lang }; pr.Stage = stage;
                foreach (string mode in new[] { "Курс", "Слабые клавиши", "Сочетания" })
                {
                    string text = Curriculum.Generate(pr, mode, new Random(42), 210);
                    Assert(text.Length >= 210 && text.All(c => c == ' ' || Curriculum.Allowed(pr).Contains(c)), "generator respects curriculum " + lang + "/" + stage + "/" + mode);
                }
            }
            Assert(Curriculum.Finger('а', "RU") == "Левый указательный" && Curriculum.Finger('j', "EN") == "Правый указательный", "home-key finger map");
            Assert(Curriculum.PhysicalKey('?', "RU") == '7' && Curriculum.PhysicalKey('?', "EN") == '/' && Curriculum.PhysicalKey('!', "EN") == '1', "shifted keys respect physical layout");
            var temp = Path.Combine(Path.GetTempPath(), "SpeedKlava-test-" + Guid.NewGuid() + ".xml");
            try
            {
                Store.Write(p, temp); var restored = Store.Read(temp);
                Assert(restored.Xp == p.Xp && restored.Sessions.Count == p.Sessions.Count && restored.Keys.Count == p.Keys.Count, "XML roundtrip");
                int oldGoal = p.GoalMinutes; p.GoalMinutes = 15;
                Store.SaveTo(p, temp);
                Assert(Store.Read(temp).GoalMinutes == 15 && Store.Read(temp + ".bak").GoalMinutes == oldGoal && !File.Exists(temp + ".tmp"), "save retains previous profile");
                File.WriteAllText(temp, "<!DOCTYPE x [<!ENTITY bad SYSTEM 'file:///C:/Windows/win.ini'>]><Profile>&bad;</Profile>");
                bool rejected = false; try { Store.Read(temp); } catch { rejected = true; }
                Assert(rejected, "external entity XML rejected");
            }
            finally { if (File.Exists(temp)) File.Delete(temp); if (File.Exists(temp + ".bak")) File.Delete(temp + ".bak"); }
            p = new Profile();
            p.Sessions.Add(new Session { At = DateTime.Today.AddDays(-1), Completed = true });
            p.Sessions.Add(new Session { At = DateTime.Today.AddDays(-2), Completed = true });
            Assert(p.Streak == 2, "yesterday streak maintained until today ends");
            p.Sessions.Add(new Session { At = DateTime.Today, Completed = true });
            Assert(p.Streak == 3, "today extends streak");
            foreach (string language in new[] { "RU", "EN" }) foreach (string mode in Expert.Modes)
            {
                if (mode == Expert.Code && language == "RU") continue;
                var beginner = new Profile { Language = language };
                string text = Expert.Generate(beginner, mode, new Random(17), 3000);
                Assert(text.Length >= 3000 && text.All(c => !char.IsControl(c)), "expert material supports full timed runs " + language + mode);
                Assert(beginner.Stage == 0, "expert generator does not unlock beginner course");
                if (mode == Expert.Speed) Assert(text.Any(c => char.IsLetter(c) && !Curriculum.Allowed(beginner).Contains(c)), "expert ignores course key restriction");
                if (mode == Expert.Rhythm) Assert(text.Split(' ').All(w => w.Length == 4), "rhythm uses uniform word length");
                if (mode == Expert.Symbols) Assert(text.Any(char.IsDigit) && text.Any(char.IsUpper) && text.Any(c => "!?;:".Contains(c)), "symbol training has digits case punctuation");
                if (mode == Expert.Code) Assert(text.Contains("{") && text.Contains(";") && text.Contains("\"") && text.Any(char.IsUpper), "code material includes syntax and case");
            }
            bool codeRejected = false; try { Expert.Generate(new Profile(), Expert.Code, new Random(1), 100); } catch (ArgumentException) { codeRejected = true; }
            Assert(codeRejected, "code requires matching EN layout");
            var personal = new Profile(); personal.Keys.Add(new KeyStat { Key = "щъ", Language = "RU", Attempts = 10, Errors = 5 });
            Assert(Expert.Generate(personal, Expert.Weak, new Random(1), 500).Split(' ').All(w => w == "щъ"), "weak-pair mode focuses recorded problem even without matching word");
            var even = new Run("aaaa", new Profile { Language = "EN" }, Expert.Rhythm);
            foreach (double time in new[] { .1, .3, .5, .7 }) { even.ActiveSeconds = time; even.Input('a'); }
            Assert(even.RhythmSamples == 3 && even.RhythmCv < .0001, "uniform intervals have zero CV");
            var uneven = new Run("aaaaaa", new Profile { Language = "EN" }, Expert.Rhythm);
            foreach (double time in new[] { .1, .2, .7, 1.7 }) { uneven.ActiveSeconds = time; uneven.Input('a'); }
            Assert(uneven.RhythmCv > 50, "uneven intervals increase CV");
            int samples = uneven.RhythmSamples;
            uneven.ActiveSeconds = 1.9; uneven.Input('b'); uneven.ActiveSeconds = 2.1; uneven.Input('a');
            Assert(uneven.RhythmSamples == samples, "error and correction break rhythm sequence");
            uneven.BreakRhythm(); uneven.ActiveSeconds = 2.5; uneven.Input('a');
            Assert(uneven.RhythmSamples == samples, "pause excludes cross-pause interval");
            var compare = new Profile();
            compare.Sessions.Add(new Session { Mode = Expert.Speed, RequestedSeconds = 60, Seconds = 60, Attempts = 9999, Correct = 9999, Completed = true });
            foreach (int speed in new[] { 200, 250, 300, 350, 1000 }) compare.Sessions.Add(new Session { Mode = Expert.Speed, RequestedSeconds = 60, Seconds = 60, Attempts = speed, Correct = speed, Completed = true });
            compare.Sessions.Add(new Session { Mode = Expert.Hard, RequestedSeconds = 60, Seconds = 60, Attempts = 999, Correct = 999, Completed = true });
            compare.Sessions.Add(new Session { Mode = Expert.Speed, Language = "EN", RequestedSeconds = 60, Seconds = 60, Attempts = 999, Correct = 999, Completed = true });
            compare.Sessions.Add(new Session { Mode = Expert.Speed, RequestedSeconds = 120, Seconds = 120, Attempts = 999, Correct = 999, Completed = true });
            compare.Sessions.Add(new Session { Mode = Expert.Speed, RequestedSeconds = 60, Seconds = 60, Attempts = 999, Correct = 300, Completed = true });
            compare.Sessions.Add(new Session { Mode = Expert.Speed, RequestedSeconds = 60, Seconds = 5, Attempts = 999, Correct = 999, Completed = true });
            compare.Sessions.Add(new Session { Mode = Expert.Speed, RequestedSeconds = 60, Seconds = 60, Attempts = 999, Correct = 999, Completed = false });
            Assert(Expert.Baseline(compare, Expert.Speed, "RU", 60) == 300, "baseline uses median of last five comparable accurate full runs only");
            Assert(Expert.Baseline(compare, Expert.Rhythm, "RU", 60) == 0, "missing baseline is not fabricated");
            var expertProfile = new Profile();
            var expertRun = Type(expertProfile, new string('а', 100), Expert.Speed, false); expertRun.RequestedSeconds = 60; expertRun.TargetCpm = 400;
            expertRun.Commit(expertProfile, true);
            var secondRun = Type(expertProfile, new string('о', 100), Expert.Speed, false); secondRun.Commit(expertProfile, true);
            Assert(expertProfile.Stage == 0 && expertProfile.Sessions[0].TargetCpm == 400 && expertProfile.Sessions[0].RhythmSamples == 99, "expert results store target and rhythm without advancing course");
            temp = Path.Combine(Path.GetTempPath(), "SpeedKlava-expert-" + Guid.NewGuid() + ".xml");
            try
            {
                expertProfile.ExpertGoal = 500; Store.Write(expertProfile, temp); var loaded = Store.Read(temp);
                Assert(loaded.ExpertGoal == 500 && loaded.Sessions[0].RequestedSeconds == 60 && loaded.Sessions[0].TargetCpm == 400 && loaded.Sessions[0].RhythmSamples == 99, "new expert fields survive save/load");
                File.WriteAllText(temp, "<Profile><Version>1</Version><RuStage>3</RuStage></Profile>");
                loaded = Store.Read(temp);
                Assert(loaded.RuStage == 3 && loaded.ExpertGoal == 300 && loaded.Sessions.Count == 0, "v1 progress loads with expert defaults");
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            var rewardProfile = new Profile(); DateTime day = new DateTime(2026, 11, 7, 12, 0, 0);
            var first = new Session { At = day, Mode = "Курс", RequestedSeconds = 60, Seconds = 30, Correct = 100, Attempts = 100, Completed = true, Xp = 45 };
            rewardProfile.Sessions.Add(first);
            var awards = Coach.Award(rewardProfile, first);
            Assert(awards.Count == 3 && first.BonusXp == 95 && rewardProfile.Xp == 140, "first accurate run earns daily accuracy and two achievements");
            Assert(Coach.Award(rewardProfile, first).Count == 0 && rewardProfile.Xp == 140 && first.BonusXp == 95, "reward claims are idempotent without double-counting XP");
            var focus = new Session { At = day.AddHours(1), Language = "EN", Mode = Expert.Weak, Seconds = 30, Correct = 100, Attempts = 100, Completed = true, Xp = 45 };
            rewardProfile.Sessions.Add(focus); Coach.Award(rewardProfile, focus);
            Assert(Coach.Missions(rewardProfile, day, true).All(m => m.Done) && focus.BonusXp == 55 && rewardProfile.Xp == 240, "daily missions combine both languages and award remaining bonuses once");
            Assert(Coach.Award(rewardProfile, focus).Count == 0, "repeat focus does not re-award daily missions");
            var tomorrow = new Session { At = day.AddDays(1), Mode = Expert.Hard, Seconds = 30, Correct = 100, Attempts = 100, Completed = true };
            rewardProfile.Sessions.Add(tomorrow); Coach.Award(rewardProfile, tomorrow);
            Assert(tomorrow.BonusXp == 50 && Coach.Missions(rewardProfile, tomorrow.At, false).First(m => m.Id == "practice").Value == 1, "missions reset by local date while lifetime rewards remain claimed");
            var returnDay = new Session { At = day.AddDays(4), Mode = "Курс", Seconds = 30, Correct = 100, Attempts = 100, Completed = true };
            rewardProfile.Sessions.Add(returnDay); Coach.Award(rewardProfile, returnDay);
            Assert(rewardProfile.Rewards.Count(reward => reward.Id == "badge:three-days") == 1, "three distinct practice days unlock reward even with gaps");
            var tiny = new Profile();
            var tooShort = new Session { At = day, Mode = "Курс", Seconds = 19, Correct = 100, Attempts = 100, Completed = true };
            tiny.Sessions.Add(tooShort);
            Assert(Coach.Award(tiny, tooShort).Count == 0 && Coach.Missions(tiny, day, false).All(m => !m.Done), "short attempts cannot farm mission or milestone rewards");
            var cancelled = new Session { At = day, Mode = Expert.Weak, Seconds = 60, Correct = 100, Attempts = 100, Completed = false };
            tiny.Sessions.Add(cancelled);
            Assert(Coach.Award(tiny, cancelled).Count == 0 && Coach.Achievements(tiny).All(a => !a.Done), "partial runs do not count toward achievements");
            var thousand = new Session { At = day, Mode = "Курс", Seconds = 200, Correct = 1000, Attempts = 1000, Completed = true };
            tiny.Sessions.Add(thousand); Coach.Award(tiny, thousand);
            Assert(Coach.Achievements(tiny).First(a => a.Id == "thousand").Done && !Coach.Achievements(tiny).First(a => a.Id == "five-thousand").Done, "milestone thresholds reflect qualified completed characters");
            Assert(Coach.Missions(new Profile(), day, true).All(m => Expert.IsMode(m.Mode)) && Coach.Missions(new Profile(), day, false).All(m => !Expert.IsMode(m.Mode)), "mission actions match beginner or expert area");
            temp = Path.Combine(Path.GetTempPath(), "SpeedKlava-reward-" + Guid.NewGuid() + ".xml");
            try
            {
                Store.Write(rewardProfile, temp); var loaded = Store.Read(temp);
                Assert(loaded.Xp == rewardProfile.Xp && loaded.Rewards.Count == rewardProfile.Rewards.Count && loaded.Sessions[0].BonusXp == first.BonusXp, "rewards and display bonuses survive save/load without duplication");
                Assert(Coach.Award(loaded, loaded.Sessions[0]).Count == 0, "reward deduplication survives reload");
                loaded.Rewards.Add(loaded.Rewards[0]); Store.Write(loaded, temp);
                bool duplicateRejected = false; try { Store.Read(temp); } catch { duplicateRejected = true; }
                Assert(duplicateRejected, "duplicate imported rewards are rejected");
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            var feedbackProfile = new Profile();
            feedbackProfile.Sessions.Add(new Session { Mode = "Курс", RequestedSeconds = 60, Seconds = 60, Correct = 90, Attempts = 100, Completed = true });
            feedbackProfile.Sessions.Add(new Session { Mode = "Курс", Language = "EN", RequestedSeconds = 60, Seconds = 60, Correct = 1, Attempts = 100, Completed = true });
            var improved = new Session { Mode = "Курс", RequestedSeconds = 60, Seconds = 60, Correct = 98, Attempts = 100, Completed = true };
            feedbackProfile.Sessions.Add(improved);
            string coaching = Coach.Feedback(feedbackProfile, improved, new Run("а", feedbackProfile, "Курс"));
            Assert(coaching.Contains("Точность выше") && coaching.Contains("8"), "coach quantifies improvement against matching language and duration");
            improved.RequestedSeconds = 30;
            Assert(!Coach.Feedback(feedbackProfile, improved, new Run("а", feedbackProfile, "Курс")).Contains("Точность выше"), "coach never invents improvement without comparable data");
            var errorRun = Type(new Profile(), "аааа", "Курс", true); var errorSession = errorRun.Commit(new Profile(), true);
            string errorFeedback = Coach.Feedback(new Profile(), errorSession, errorRun);
            Assert(errorFeedback.Contains("а (1)") && errorFeedback.Contains("Следующий шаг"), "coach identifies actual expected-key errors and next step");
            Assert(Coach.Feedback(new Profile(), cancelled, errorRun).Contains("сохранено"), "partial-run coaching acknowledges saved work");
            foreach (string language in new[] { "RU", "EN" }) for (int stage = 0; stage < Curriculum.Count; stage++)
                Assert(Coach.Lesson(new Profile { Language = language }, stage).Length > 150, "concrete lesson available at each language and stage");
            foreach (string mode in Expert.Modes) Assert(Coach.Tip(new Profile(), mode).Length > 150, "expert technique coaching available " + mode);
            Console.WriteLine("PASS: " + assertions + " assertions"); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
