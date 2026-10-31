using System.IO;
using System.Text;
using ATTM.Core.Midi;
using ATTM.Core.Syntax;
using ATTM.Editing;
using ATTM.Services;

namespace ATTM.Tests;

/// <summary>
/// 零依赖断言测试：dotnet run --project tests/ATTM.Tests
/// 退出码 0 = 全绿，非 0 = 有失败。
/// </summary>
internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length >= 3 && args[0] == "--export")
        {
            var result = AttmParser.Parse(File.ReadAllText(args[1]));
            foreach (var diagnostic in result.Diagnostics)
                Console.WriteLine($"  {diagnostic.Line}:{diagnostic.Column} E{diagnostic.CodeNumber} {diagnostic.Message}");

            if (result.Score is null || result.HasErrors)
            {
                Console.WriteLine("存在语法错误，拒绝导出");
                return 2;
            }

            MidiFileWriter.Write(result.Score, args[2]);
            Console.WriteLine($"已导出 {args[2]}（{result.Score.Notes.Count} 个音符，{result.Score.Duration:mm\\:ss}）");
            return 0;
        }

        const string Header = "bpm = 120\n1 = C 4/4\n\n";

        // ---------------------------------------------------------- 词法

        Check("词法：注释行", () =>
        {
            var tokens = AttmLexer.Lex("# 这是一行注释");
            return tokens.Count == 1 && tokens[0].Kind == TokenKind.Comment;
        });

        Check("词法：#4 是升号而不是注释", () =>
        {
            var tokens = AttmLexer.Lex("#4");
            return tokens.Count == 2
                   && tokens[0].Kind == TokenKind.Sharp
                   && tokens[1].Kind == TokenKind.Number;
        });

        Check("词法：每个数字字符单独成 token", () =>
        {
            var tokens = AttmLexer.Lex("1134");
            return tokens.Count == 4 && tokens.All(t => t.Kind == TokenKind.Number);
        });

        Check("词法：行尾注释", () =>
        {
            var tokens = AttmLexer.Lex("1,2 # ok");
            return tokens[^1].Kind == TokenKind.Comment && tokens[^1].Text == "# ok";
        });

        // ---------------------------------------------------------- 指令

        Check("指令：bpm 支持小数", () =>
        {
            var r = AttmParser.Parse("bpm = 120.5\n1 = C 4/4\n\n1");
            return r.Score is { Bpm: 120.5 } && !r.HasErrors;
        });

        Check("指令：bpm = 120 报 E3", () =>
        {
            var r = AttmParser.Parse("bpm=120\n1 = C 4/4\n\n1");
            return HasCode(r, DiagnosticCode.DirectiveSyntax);
        });

        Check("指令：缺少 bpm 报 E1", () =>
        {
            var r = AttmParser.Parse("1 = C 4/4\n\n1");
            return HasCode(r, DiagnosticCode.MissingBpm) && r.Score is null;
        });

        Check("指令：缺少调式报 E2", () =>
        {
            var r = AttmParser.Parse("bpm = 120\n\n1");
            return HasCode(r, DiagnosticCode.MissingKeySignature) && r.Score is null;
        });

        Check("指令：调式必须 A-G（E110）", () =>
        {
            var r = AttmParser.Parse("bpm = 120\n1 = H 4/4\n\n1");
            return HasCode(r, DiagnosticCode.InvalidKeyRoot);
        });

        Check("指令：只支持 4/4 与 3/4（E111）", () =>
        {
            var r = AttmParser.Parse("bpm = 120\n1 = C 6/8\n\n1");
            return HasCode(r, DiagnosticCode.InvalidTimeSignature);
        });

        Check("指令：bpm 重复报 E4", () =>
        {
            var r = AttmParser.Parse("bpm = 120\nbpm = 90\n1 = C 4/4\n\n1");
            return HasCode(r, DiagnosticCode.DuplicateDirective);
        });

        Check("指令：出现在小节之后报 E5", () =>
        {
            var r = AttmParser.Parse("bpm = 120\n1 = C 4/4\n\n1\n\nbpm = 90");
            return HasCode(r, DiagnosticCode.DirectiveAfterContent);
        });

        Check("指令：Vel 解析", () =>
        {
            var r = AttmParser.Parse("bpm = 120\n1 = C 4/4\nVel = 100 +-20\n\n1");
            return r.Score is { VelocityBase: 100, VelocityJitter: 20 } && !r.HasErrors;
        });

        Check("指令：Vel 省略时默认 90 +-0", () =>
        {
            var r = Parse(Header, "1");
            return r.Score is { VelocityBase: 90, VelocityJitter: 0 };
        });

        Check("指令：Vel 超范围报 E109", () =>
        {
            var r = AttmParser.Parse("bpm = 120\n1 = C 4/4\nVel = 200\n\n1");
            return HasCode(r, DiagnosticCode.VelocityOutOfRange);
        });

        // ---------------------------------------------------------- 计划书 5.7 表格

        Check("时值：`1` 是全音符", () =>
            Notes(Parse(Header, "1"), (60, 0, 1920)));

        Check("时值：`1,0` 被休止截断为半音符", () =>
            Notes(Parse(Header, "1,0"), (60, 0, 960)));

        Check("时值：`1,0,0,0` 被休止截断为四分音符", () =>
            Notes(Parse(Header, "1,0,0,0"), (60, 0, 480)));

        Check("时值：`3,3,4,5`", () =>
            Notes(Parse(Header, "3,3,4,5"),
                (64, 0, 480), (64, 480, 480), (65, 960, 480), (67, 1440, 480)));

        Check("时值：`11,12,3,05`", () =>
            Notes(Parse(Header, "11,12,3,05"),
                (60, 0, 240), (60, 240, 240), (60, 480, 240), (62, 720, 240),
                (64, 960, 480), (67, 1680, 240)));

        Check("时值：`1134,5,12,0057`", () =>
            Notes(Parse(Header, "1134,5,12,0057"),
                (60, 0, 120), (60, 120, 120), (64, 240, 120), (65, 360, 120),
                (67, 480, 480),
                (60, 960, 240), (62, 1200, 240),
                (67, 1680, 120), (71, 1800, 120)));

        Check("时值：`3,02,2,0`", () =>
            Notes(Parse(Header, "3,02,2,0"),
                (64, 0, 480), (62, 720, 240), (62, 960, 480)));

        Check("休止：`0,0,0,0` 合法且无音符", () =>
        {
            var r = Parse(Header, "0,0,0,0");
            return !r.HasErrors && r.Score is { Notes.Count: 0 } and { MeasureCount: 1 };
        });

        // ---------------------------------------------------------- 和弦

        Check("和弦：`(46+1),(57+2),(357),(6+1+3)`", () =>
            Notes(Parse(Header, "(46+1),(57+2),(357),(6+1+3)"),
                (65, 0, 480), (69, 0, 480), (72, 0, 480),
                (67, 480, 480), (71, 480, 480), (74, 480, 480),
                (64, 960, 480), (67, 960, 480), (71, 960, 480),
                (69, 1440, 480), (72, 1440, 480), (76, 1440, 480)));

        Check("和弦：反拍 `0(46+1),...`", () =>
            Notes(Parse(Header, "0(46+1),0(57+2),(357),(6+1+3)"),
                (65, 240, 240), (69, 240, 240), (72, 240, 240),
                (67, 720, 240), (71, 720, 240), (74, 720, 240),
                (64, 960, 480), (67, 960, 480), (71, 960, 480),
                (69, 1440, 480), (72, 1440, 480), (76, 1440, 480)));

        Check("和弦：组内音共享源码区间", () =>
        {
            var r = Parse(Header, "(46+1)");
            var notes = r.Score!.Notes;
            return notes.Count == 3 && notes[0].Span == notes[1].Span && notes[1].Span == notes[2].Span;
        });

        // ---------------------------------------------------------- 并行小节

        Check("并行：三行同时演奏", () =>
        {
            var r = Parse(Header, "4,5,3,6\n6,7,5,+1\n+1,+2,7,+3");
            return !r.HasErrors
                   && r.Score is { MeasureCount: 1, Notes.Count: 12 }
                   && r.Score.Notes.Count(n => n.VoiceIndex == 0) == 4
                   && r.Score.Notes.Count(n => n.VoiceIndex == 1) == 4
                   && r.Score.Notes.Count(n => n.VoiceIndex == 2) == 4;
        });

        Check("并行：空行分隔出两个小节", () =>
        {
            var r = Parse(Header, "1\n\n1");
            return r.Score is { MeasureCount: 2 }
                   && Notes(r, (60, 0, 1920), (60, 1920, 1920));
        });

        Check("并行：注释行不打断小节组", () =>
        {
            var r = Parse(Header, "4,5,3,6\n# 注释\n6,7,5,+1");
            return r.Score is { MeasureCount: 1, Notes.Count: 8 };
        });

        Check("并行：同音高重叠被覆盖", () =>
        {
            var r = Parse(Header, "1\n1");
            return r.Score is { Notes.Count: 1 };
        });

        // ---------------------------------------------------------- 3/4

        Check("3/4：`1,2,3` 合法（三个四分音符）", () =>
        {
            var r = AttmParser.Parse("bpm = 120\n1 = C 3/4\n\n1,2,3");
            return !r.HasErrors
                   && Notes(r, (60, 0, 480), (62, 480, 480), (64, 960, 480));
        });

        Check("3/4：`1,2` 合法（附点二分音符切成两段）", () =>
        {
            var r = AttmParser.Parse("bpm = 120\n1 = C 3/4\n\n1,2");
            return !r.HasErrors && Notes(r, (60, 0, 720), (62, 720, 720));
        });

        Check("3/4：3 个逗号合法", () =>
        {
            var r = AttmParser.Parse("bpm = 120\n1 = C 3/4\n\n1,2,3,4");
            return !r.HasErrors
                   && Notes(r, (60, 0, 360), (62, 360, 360), (64, 720, 360), (65, 1080, 360));
        });

        Check("4/4：2 个逗号报 E108", () =>
            HasCode(Parse(Header, "1,2,3"), DiagnosticCode.InvalidSegmentCount));

        // ---------------------------------------------------------- 错误

        Check("错误：段内个数不是 2 的幂（E107）", () =>
            HasCode(Parse(Header, "123"), DiagnosticCode.UnitCountNotPowerOfTwo));

        Check("错误：空段（E114）", () =>
            HasCode(Parse(Header, "1,,2"), DiagnosticCode.EmptySegment));

        Check("错误：行尾逗号（E114）", () =>
            HasCode(Parse(Header, "1,"), DiagnosticCode.EmptySegment));

        Check("错误：括号未闭合（E104）", () =>
            HasCode(Parse(Header, "(46+1"), DiagnosticCode.UnclosedParenthesis));

        Check("错误：空和弦（E105）", () =>
            HasCode(Parse(Header, "()"), DiagnosticCode.EmptyChord));

        Check("错误：和弦里有 0（E106）", () =>
            HasCode(Parse(Header, "(406)"), DiagnosticCode.RestInChord));

        Check("错误：数字 8 非法（E101）", () =>
            HasCode(Parse(Header, "8"), DiagnosticCode.InvalidDigit));

        Check("错误：`+` 后没有数字（E117）", () =>
            HasCode(Parse(Header, "1+"), DiagnosticCode.OctaveMarkerWithoutDigit));

        Check("错误：`#` 后接空格是注释，不产生音符", () =>
        {
            var r = Parse(Header, "1,2,3,4\n\n# 4 5");
            return !r.HasErrors && r.Score is { Notes.Count: 4 };
        });

        Check("八度：`++1` 合法（= 高两个八度）", () =>
            !Parse(Header, "++1").HasErrors);

        Check("错误：混用八度标记 `+-1` 报 E102", () =>
            HasCode(Parse(Header, "+-1"), DiagnosticCode.InvalidCharacter));

        // ---------------------------------------------------------- 音高映射

        Check("音高：1 = D 时 1/+1/-1", () =>
        {
            var r = AttmParser.Parse("bpm = 120\n1 = D 4/4\n\n1\n\n+1\n\n-1");
            return Notes(r, (62, 0, 1920), (74, 1920, 1920), (50, 3840, 1920));
        });

        Check("音高：1 = B 时 7/+/#", () =>
        {
            var r = AttmParser.Parse("bpm = 120\n1 = B 4/4\n\n7\n\n#7\n\n+1");
            return Notes(r, (82, 0, 1920), (83, 1920, 1920), (83, 3840, 1920));
        });

        Check("音高：超出范围报 E115", () =>
        {
            // 本语法单次只能 ±1 个八度，正常输入不可达；直接验证映射器的边界保护
            return !PitchMapper.TryMap('C', 1, 6, false, out _)
                   && PitchMapper.TryMap('C', 1, 5, false, out var top) && top == 120;
        });

        // ---------------------------------------------------------- 位置换算

        Check("位置：偏移换算行列", () =>
        {
            const string text = "bpm = 120\n1 = C 4/4\n\n1,2,3,4";
            var r = AttmParser.Parse(text);
            var offset = text.IndexOf("2,3", StringComparison.Ordinal);
            var (line, column) = r.GetLineColumn(offset);
            return line == 4 && column == 3;
        });

        // ---------------------------------------------------------- 带错误的乐谱

        Check("容错：语法错误时仍可得到部分乐谱", () =>
        {
            var r = Parse(Header, "1,2,3,4\n\n123");
            return r.Score is not null && r.HasErrors && r.Score.Notes.Count == 4;
        });

        // ---------------------------------------------------------- MIDI 导出

        Check("MIDI：导出后回读一致（`3,02,2,0`）", () =>
        {
            var r = Parse(Header, "3,02,2,0");
            if (r.Score is null) return false;

            var path = Path.Combine(Path.GetTempPath(), "attm-test-1.mid");
            MidiFileWriter.Write(r.Score, path, random: null);
            var midi = MidiReader.Read(path);
            File.Delete(path);

            var expected = new[]
            {
                new MidiReader.RawNote(0, 480, 64, 90),
                new MidiReader.RawNote(720, 240, 62, 90),
                new MidiReader.RawNote(960, 480, 62, 90)
            };

            return midi.TicksPerQuarter == 480 && midi.Notes.SequenceEqual(expected);
        });

        Check("MIDI：文件头为 PPQ 480 / 120bpm / 4-4", () =>
        {
            var r = Parse(Header, "1");
            if (r.Score is null) return false;

            var path = Path.Combine(Path.GetTempPath(), "attm-test-2.mid");
            MidiFileWriter.Write(r.Score, path);
            var midi = MidiReader.Read(path);
            File.Delete(path);

            return midi.Format == 0
                   && midi.TrackCount == 1
                   && midi.TicksPerQuarter == 480
                   && midi.TempoMicroseconds == 500_000
                   && midi.BeatsPerMeasure == 4
                   && midi.BeatUnit == 4;
        });

        Check("MIDI：3/4 拍号写入正确", () =>
        {
            var r = AttmParser.Parse("bpm = 90\n1 = G 3/4\n\n1,2,3");
            if (r.Score is null) return false;

            var path = Path.Combine(Path.GetTempPath(), "attm-test-3.mid");
            MidiFileWriter.Write(r.Score, path);
            var midi = MidiReader.Read(path);
            File.Delete(path);

            return midi.BeatsPerMeasure == 3
                   && midi.BeatUnit == 4
                   && midi.TempoMicroseconds == 666_667
                   && midi.Notes.Count == 3
                   && midi.Notes[0].Pitch == 67;
        });

        Check("MIDI：力度抖动被限制在 1-127", () =>
        {
            var random = new Random(1234);
            var ok = true;
            for (var i = 0; i < 400; i++)
            {
                var velocity = MidiSequence.ApplyJitter(120, 40, random);
                if (velocity is < 1 or > 127) { ok = false; break; }
            }

            return ok && MidiSequence.ApplyJitter(90, 0, null) == 90;
        });

        // ---------------------------------------------------------- 文本缓冲

        Check("缓冲：插入与行映射", () =>
        {
            var buffer = new TextBuffer("abc\ndef");
            buffer.Replace(1, 0, "XY");
            if (buffer.Text != "aXYbc\ndef") return false;
            if (buffer.LineCount != 2) return false;
            if (buffer.GetLineColumn(5) != (1, 6)) return false;
            if (buffer.GetOffset(1, 2) != 8) return false;
            return buffer.GetLine(1) == "def";
        });

        Check("缓冲：删除跨行文本", () =>
        {
            var buffer = new TextBuffer("abc\ndef");
            buffer.Replace(2, 3, "");
            return buffer.Text == "abef" && buffer.LineCount == 1;
        });

        // ---------------------------------------------------------- 语法高亮分区

        Check("高亮：注释 / 指令 / 音符 / 休止 / 升降号", () =>
        {
            var text = "# hi\nbpm = 120\n1 = C 4/4\n\n1,0,+#4";
            var r = AttmParser.Parse(text);
            var runs = EditorHighlighter.Build(r);

            static int Total(List<HighlightRun> runs, EditorTokenClass cls)
                => runs.Where(x => x.Class == cls).Sum(x => x.Length);

            return Total(runs[0], EditorTokenClass.Comment) > 0
                   && Total(runs[1], EditorTokenClass.Directive) == 3
                   && Total(runs[1], EditorTokenClass.Value) == 3
                   && Total(runs[4], EditorTokenClass.Note) == 2
                   && Total(runs[4], EditorTokenClass.Rest) == 1
                   && Total(runs[4], EditorTokenClass.Octave) == 1
                   && Total(runs[4], EditorTokenClass.Sharp) == 1;
        });

        Check("高亮：行号与源码行一一对应", () =>
        {
            var text = "bpm = 120\n1 = C 4/4\n\n1,2,3,4\n\n3,02,2,0";
            var r = AttmParser.Parse(text);
            var runs = EditorHighlighter.Build(r);
            return runs.Length == r.LineStarts.Count && runs.Length == 6;
        });

        Check("主题：配置字符串与枚举映射正确", () =>
            ThemeService.ParseMode("Dark") == AppTheme.Dark
            && ThemeService.ParseMode("Light") == AppTheme.Light
            && ThemeService.ParseMode("System") == AppTheme.System
            && ThemeService.ToConfigString(AppTheme.Light) == "Light"
            && ThemeService.ToConfigString(AppTheme.System) == "System");

        Check("主题：Light 配置不会被判成深色", () =>
        {
            var config = new AppConfig { Theme = "Light" };
            return !config.IsDark && config.Mode == AppTheme.Light;
        });

        Check("配置：默认字号已放大到 18", () => Math.Abs(new AppConfig().EditorFontSize - 18) < 0.01);

        Check("文件：UTF-8 无 BOM + LF 往返", () =>
        {
            const char cr = (char)13;
            const char lf = (char)10;

            var path = Path.Combine(Path.GetTempPath(), "attm-io-roundtrip.attm");
            TextFile.Write(path, "bpm = 120" + cr + lf + "1 = C 4/4" + lf + "1,2,3");

            var text = TextFile.Read(path);
            var bytes = File.ReadAllBytes(path);
            File.Delete(path);

            return text == "bpm = 120" + lf + "1 = C 4/4" + lf + "1,2,3"
                   && bytes[0] != 0xEF;
        });

        Check("文件：BOM 与 GBK 老文件都能读", () =>
        {
            const char lf = (char)10;

            var bom = Path.Combine(Path.GetTempPath(), "attm-io-bom.attm");
            File.WriteAllBytes(bom, new byte[] { 0xEF, 0xBB, 0xBF }
                .Concat(Encoding.UTF8.GetBytes("1,2,3,4")).ToArray());
            var bomOk = TextFile.Read(bom) == "1,2,3,4";
            File.Delete(bom);

            var gbkOk = true;
            try
            {
                var gbkPath = Path.Combine(Path.GetTempPath(), "attm-io-gbk.attm");
                File.WriteAllBytes(gbkPath, Encoding.GetEncoding(936).GetBytes("# 欢乐颂" + lf + "1,2,3"));
                gbkOk = TextFile.Read(gbkPath).Contains("欢乐颂");
                File.Delete(gbkPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("      GBK 不可用：" + ex.GetType().Name);
            }

            return bomOk && gbkOk;
        });

        Check("配置：默认保存目录是 音乐/ATTM", () =>
        {
            var folder = AppConfig.ResolveSaveFolder(new AppConfig());
            var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            var expected = Path.Combine(music, "ATTM");
            Console.WriteLine("      " + folder);
            return folder == expected && Directory.Exists(folder);
        });

        Check("性能：两千行谱面解析在 1 秒内", () =>
        {
            const char lf = (char)10;
            var builder = new StringBuilder("bpm = 120" + lf + "1 = C 4/4" + lf + lf);
            for (var i = 0; i < 500; i++)
                builder.Append("1,2,3,4" + lf + lf);

            var text = builder.ToString();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var result = AttmParser.Parse(text);
            watch.Stop();

            Console.WriteLine($"      {result.Score?.Notes.Count ?? 0} 音符，{watch.ElapsedMilliseconds} ms");
            return result.Score is not null && watch.ElapsedMilliseconds < 1000;
        });

        Check("文件：保存同时产出 .attm 和 .mid", () =>
        {
            const char lf = (char)10;

            var dir = Path.Combine(Path.GetTempPath(), "attm-save-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            var attmPath = Path.Combine(dir, "测试.attm");
            var midiPath = Path.ChangeExtension(attmPath, ".mid");
            var text = "bpm = 120" + lf + "1 = C 4/4" + lf + lf + "1,2,3,4";

            TextFile.Write(attmPath, text);

            var result = AttmParser.Parse(TextFile.Read(attmPath));
            if (result.Score is null) return false;

            MidiFileWriter.Write(result.Score, midiPath);

            var midi = File.ReadAllBytes(midiPath);
            var ok = midi.Length > 30
                     && Encoding.ASCII.GetString(midi, 0, 4) == "MThd"
                     && TextFile.Read(attmPath) == text;

            Directory.Delete(dir, true);
            return ok;
        });

        Check("教程：文档里的示例谱面都能解析", () =>
        {
            const char lf = (char)10;
            string J(params string[] lines) => string.Join(lf, lines);

            var examples = new (string Name, string Text)[]
            {
                ("欢乐颂片段", J("bpm = 120", "1 = C 4/4", "",
                    "1,2,3,4", "", "3,02,2,0", "", "3,4,5,4", "", "3,02,2,0")),
                ("八度与升号", J("bpm = 120", "1 = C 4/4", "", "1,+1,2,-2", "#4,+#4,1,2")),
                ("三种等价的时值", J("bpm = 120", "1 = C 4/4", "", "1", "", "1,0", "", "1,0,0,0")),
                ("四分与八分", J("bpm = 120", "1 = C 4/4", "", "11,12,3,05")),
                ("十六分混排", J("bpm = 120", "1 = C 4/4", "", "1134,5,12,0057")),
                ("一小节十六个音", J("bpm = 120", "1 = C 4/4", "", "1111,2222,3333,4444")),
                ("和弦正拍", J("bpm = 90", "1 = C 4/4", "", "(46+1),(57+2),(357),(6+1+3)")),
                ("和弦反拍", J("bpm = 90", "1 = C 4/4", "", "0(46+1),0(57+2),0(357),0(6+1+3)")),
                ("三和弦双声部", J("bpm = 100", "1 = C 4/4", "",
                    "5,6,7,+1", "(1+3+5),(2+4+6),(3+5+7),(4+6+1)", "",
                    "5,4,3,2", "(1+3+5),(2+4+6),(3+5+7),(4+6+1)")),
                ("注释不打断分组", J("bpm = 120", "1 = C 4/4", "",
                    "1,2,3,4", "# 注释", "5,6,7,+1")),
                ("3/4 拍", J("bpm = 100", "1 = C 3/4", "", "1", "", "1,0", "", "1,0,0", "", "1,0,0,0")),
                ("1 = D 调式", J("bpm = 120", "1 = D 4/4", "", "3,4,5,6")),
                ("1 = B 调式", J("bpm = 120", "1 = B 4/4", "", "1,2,3,4")),
                ("力度抖动", J("bpm = 120", "1 = C 4/4", "Vel = 100 +-20", "", "1,2,3,4")),
                ("小数 bpm", J("bpm = 92.5", "1 = C 4/4", "", "1,2,3,4"))
            };

            var ok = true;
            foreach (var (name, text) in examples)
            {
                var result = AttmParser.Parse(text);
                if (result.CanPlay) continue;

                ok = false;
                var first = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
                Console.WriteLine($"      {name} 失败：{first?.Message}");
            }

            return ok;
        });

        Check("调式：升号调式 #C / C# 都支持", () =>
        {
            const char lf = (char)10;

            var prefix = AttmParser.Parse("bpm = 120" + lf + "1 = #C 4/4" + lf + lf + "1,4,5,7");
            var suffix = AttmParser.Parse("bpm = 120" + lf + "1 = C# 4/4" + lf + lf + "1,4,5,7");

            if (!prefix.CanPlay || !suffix.CanPlay) return false;

            var a = prefix.Score!.Notes.Select(n => n.Pitch).ToArray();
            var b = suffix.Score!.Notes.Select(n => n.Pitch).ToArray();

            Console.WriteLine($"      #C: {string.Join(",", a)}  C#: {string.Join(",", b)}  {prefix.Score.KeyName}");
            return a.SequenceEqual(new[] { 61, 66, 68, 72 })
                   && b.SequenceEqual(a)
                   && prefix.Score.KeyName == "C# 大调";
        });

        Check("调式：升号调式再加 # 是双重升号", () =>
        {
            const char lf = (char)10;

            var result = AttmParser.Parse("bpm = 120" + lf + "1 = #F 4/4" + lf + lf + "1,#1,4,5");
            if (!result.CanPlay) return false;

            var pitches = result.Score!.Notes.Select(n => n.Pitch).ToArray();
            Console.WriteLine($"      #F: 1,#1,4,5 -> {string.Join(",", pitches)}  {result.Score.KeyName}");

            return pitches.SequenceEqual(new[] { 66, 67, 71, 73 });
        });

        Check("调式：非法根音仍然报错", () =>
        {
            const char lf = (char)10;

            var bad = AttmParser.Parse("bpm = 120" + lf + "1 = #H 4/4" + lf + lf + "1,2,3,4");
            var ok = AttmParser.Parse("bpm = 120" + lf + "1 = #B 4/4" + lf + lf + "1,2,3,4");

            return !bad.CanPlay
                   && bad.Diagnostics.Any(d => d.Code == DiagnosticCode.InvalidKeyRoot)
                   && ok.CanPlay
                   && ok.Score!.Notes[0].Pitch == 72;
        });

        Check("音符：+/- 可以叠加表示多个八度", () =>
        {
            const char lf = (char)10;

            var result = AttmParser.Parse("bpm = 120" + lf + "1 = C 4/4" + lf + lf
                + "++1,--1,+++#1,--#1");

            if (!result.CanPlay)
            {
                Console.WriteLine("      " + result.Diagnostics.FirstOrDefault()?.Message);
                return false;
            }

            var pitches = result.Score!.Notes.Select(n => n.Pitch).ToArray();
            Console.WriteLine($"      ++1,--1,+++#1,--#1 -> {string.Join(",", pitches)}");

            return pitches.SequenceEqual(new[] { 84, 36, 97, 37 });
        });

        Check("音符：混用 +/- 会报错", () =>
        {
            const char lf = (char)10;

            var mixed = AttmParser.Parse("bpm = 120" + lf + "1 = C 4/4" + lf + lf + "+-1,2,3,4");
            var rest = AttmParser.Parse("bpm = 120" + lf + "1 = C 4/4" + lf + lf + "++0,1,1,1");

            return mixed.Diagnostics.Any(d => d.Message.Contains("不能混用"))
                   && rest.Diagnostics.Any(d => d.Message.Contains("休止符 0"));
        });

        PlaybackChecks.Run();

        Console.WriteLine();
        Console.WriteLine($"通过 {_passed}，失败 {_failed}");
        return _failed == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------ 工具

    private static ParseResult Parse(string header, string body) => AttmParser.Parse(header + body);

    private static bool HasCode(ParseResult result, DiagnosticCode code)
        => result.Diagnostics.Any(d => d.Code == code);

    private static bool Notes(ParseResult result, params (int Pitch, long Start, long Duration)[] expected)
    {
        var notes = result.Score?.Notes;
        if (notes is null || notes.Count != expected.Length)
        {
            if (notes is not null)
            {
                Console.WriteLine($"      实际音符数 {notes.Count}，期望 {expected.Length}");
                foreach (var note in notes)
                    Console.WriteLine($"      {note.Pitch}@{note.StartTicks}/{note.DurationTicks}");
            }

            return false;
        }

        for (var i = 0; i < expected.Length; i++)
        {
            if (notes[i].Pitch == expected[i].Pitch
                && notes[i].StartTicks == expected[i].Start
                && notes[i].DurationTicks == expected[i].Duration)
            {
                continue;
            }

            Console.WriteLine(
                $"      第 {i} 个音符：实际 {notes[i].Pitch}@{notes[i].StartTicks}/{notes[i].DurationTicks}，" +
                $"期望 {expected[i].Pitch}@{expected[i].Start}/{expected[i].Duration}");
            return false;
        }

        return true;
    }

    internal static void Check(string name, Func<bool> test)
    {
        try
        {
            if (test())
            {
                _passed++;
                Console.WriteLine($"  ok   {name}");
            }
            else
            {
                _failed++;
                Console.WriteLine($"  FAIL {name}");
            }
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine($"  FAIL {name}：{ex.GetType().Name} {ex.Message}");
        }
    }
}
