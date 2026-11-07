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
            Console.WriteLine("PASS: " + assertions + " assertions"); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
