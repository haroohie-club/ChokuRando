﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using DynamicData;
using HaruhiChokuretsuLib.Archive;
using HaruhiChokuretsuLib.Archive.Event;
using HaruhiChokuretsuLib.Util;
using ListRandomizer;
using Mono.Options;
using ReactiveUI;
using SerialLoops.Lib;
using SerialLoops.Lib.Items;
using SerialLoops.Lib.Script;
using SerialLoops.Lib.Script.Parameters;

namespace ChokuRando;

class Program
{
    public static void Main(string[] args)
    {
        string inputRom = string.Empty, outputRom = string.Empty, devkitArm = string.Empty;
        bool scenario = false, groupSelections = false, topics = false, puzzles = false, crf = false;
        
        OptionSet options = new()
        {
            { "i|input=", "Input Chokuretsu ROM", i => inputRom = i },
            { "o|output=", "Output randomized ROM", o => outputRom = o },
            { "d|devkitarm=", "Path to devkitARM (used for assembling files)", d => devkitArm = d },
            { "s|scenario", "Randomize the scenario (re-ordering the script files", _ => scenario = true },
            { "g|group-selections", "Randomize group selections (swapping activities between selections)", _ => groupSelections = true },
            { "t|topics", "Randomize topics (changing which topics are obtained in all scripts)", _ => topics = true },
            { "p|puzzles", "Randomize puzzles (shuffle maps, singularities, and distributions)", _ => puzzles = true },
            { "c|cut-content", "Include cut content (unused maps and scripts)", _ => crf = true },
        };

        options.Parse(args);

        if (string.IsNullOrEmpty(inputRom) || string.IsNullOrEmpty(outputRom))
        {
            Console.WriteLine("You must specify input and output ROM files.");
            return;
        }

        if (string.IsNullOrEmpty(devkitArm))
        {
            Console.WriteLine("The devkitARM directory must be specified in order to build the ROM.");
            return;
        }

        Config config = new()
        {
            UserDirectory = Path.GetDirectoryName(outputRom),
            ConfigPath = Path.Combine(Path.GetDirectoryName(outputRom)!, "SerialLoops", "config.json"),
            DevkitArmPath = devkitArm,
        };
        if (Directory.Exists(Path.Combine(config.UserDirectory!, "Projects", "Rando")))
        {
            Directory.Delete(Path.Combine(config.UserDirectory!, "Projects", "Rando"), true);
        }

        ConsoleLogger log = new();
        ConsoleProgressTracker tracker = new();
        
        byte[] romData = File.ReadAllBytes(inputRom);
        string langCode = string.Join("", SHA1.Create().ComputeHash(romData).Select(b => $"{b:X2}"))
            .Equals("81D5C6316DBCEF9F4C51984ADCAAE171124EBB08", StringComparison.OrdinalIgnoreCase) ? "ja" : "en";
        Project project = new("Rando", langCode, config, s => s, log);

        Console.WriteLine("Loading ROM...");
        SerialLoops.Lib.IO.OpenRom(project, inputRom, log, tracker);
        project.Load(config, log, tracker);
        Console.WriteLine("ROM loaded. Randomizing...");
        
        Dictionary<string, IncludeEntry[]> includes = new()
        {
            {
                "GRPBIN",
                project.Grp.GetSourceInclude().Split('\n').Where(s => !string.IsNullOrEmpty(s))
                    .Select(i => new IncludeEntry(i)).ToArray()
            },
            {
                "DATBIN",
                project.Dat.GetSourceInclude().Split('\n').Where(s => !string.IsNullOrEmpty(s))
                    .Select(i => new IncludeEntry(i)).ToArray()
            },
            {
                "EVTBIN",
                project.Evt.GetSourceInclude().Split('\n').Where(s => !string.IsNullOrEmpty(s))
                    .Select(i => new IncludeEntry(i)).ToArray()
            },
        };
        
        if (scenario)
        {
            Console.WriteLine("Randomizing scenario...");
            List<(int idx, ScenarioCommand cmd)> cmdsWithIndices = project.Scenario.Commands
                .Select((c, i) => (i, c))
                .Where(s => s.c.Verb == ScenarioCommand.ScenarioVerb.LOAD_SCENE).ToList();
            List<int> indices = cmdsWithIndices.Select(s => s.idx).ToList();
            List<ScenarioCommand> loadSceneCommands = cmdsWithIndices.Select(s => s.cmd).ToList();
            indices.Shuffle();
            for (int i = 0; i < cmdsWithIndices.Count; i++)
            {
                project.Scenario.Commands[indices[i]] = loadSceneCommands[i];
            }

            // Mark the tutorials as seen in the first script
            ScriptItem firstScript = (ScriptItem)project.Items.First(i =>
                i.Type == ItemDescription.ItemType.Script &&
                ((ScriptItem)i).Event.Index == project.Scenario.Commands
                    .First(c => c.Verb == ScenarioCommand.ScenarioVerb.LOAD_SCENE).Parameter);
            firstScript.Event.ScriptSections[0].Objects.InsertRange(0,
                [
                    new(new()
                    {
                        CommandId = (int)EventFile.CommandVerb.FLAG,
                        Mnemonic = EventFile.CommandVerb.FLAG.ToString(),
                        Parameters = new string[2],
                    }),
                    new(new()
                    {
                        CommandId = (int)EventFile.CommandVerb.FLAG,
                        Mnemonic = EventFile.CommandVerb.FLAG.ToString(),
                        Parameters = new string[2],
                    }),
                    new(new()
                    {
                        CommandId = (int)EventFile.CommandVerb.FLAG,
                        Mnemonic = EventFile.CommandVerb.FLAG.ToString(),
                        Parameters = new string[2],
                    }),
                    new(new()
                    {
                        CommandId = (int)EventFile.CommandVerb.FLAG,
                        Mnemonic = EventFile.CommandVerb.FLAG.ToString(),
                        Parameters = new string[2],
                    }),
                    new(new()
                    {
                        CommandId = (int)EventFile.CommandVerb.FLAG,
                        Mnemonic = EventFile.CommandVerb.FLAG.ToString(),
                        Parameters = new string[2],
                    }),
                ]);
            for (int i = 0; i < 5; i++)
            {
                firstScript.Event.ScriptSections[0].Objects[i].Parameters =
                [
                    (short)(1011 + i + 1), 1,
                    .. new short[14],
                ];
            }
            SerialLoops.Lib.IO.WriteStringFile(Path.Combine("assets", "events", $"{firstScript.Event.Index:X3}.s"), firstScript.Event.GetSource(includes), project, log);
        }

        if (groupSelections)
        {
            Console.WriteLine("Randomizing group selections...");
            List<(int idx, ScenarioCommand cmd)> cmdsWithIndices = project.Scenario.Commands
                .Select((c, i) => (i, c))
                .Where(s => s.c.Verb == ScenarioCommand.ScenarioVerb.ROUTE_SELECT).ToList();
            List<int> indices = cmdsWithIndices.Select(s => s.idx).ToList();
            List<ScenarioCommand> routeSelectCommands = cmdsWithIndices.Select(s => s.cmd).ToList();
            indices.Shuffle();
            for (int i = 0; i < cmdsWithIndices.Count; i++)
            {
                project.Scenario.Commands[indices[i]] = routeSelectCommands[i];
            }

            List<GroupSelectionItem> groupSelectionItems = project.Items
                .Where(i => i.Type == ItemDescription.ItemType.Group_Selection)
                .Cast<GroupSelectionItem>().ToList();
            List<ScenarioActivity> activities = groupSelectionItems.SelectMany(g => g.Selection.Activities).Where(a => a is not null && !a.HaruhiPresent).ToList();
            List<ScenarioActivity> haruhiActivities = groupSelectionItems.SelectMany(g => g.Selection.Activities).Where(a => a is not null && a.HaruhiPresent).ToList();
            
            // List<ScenarioRoute> routes = activities.SelectMany(a => a.Routes).ToList();
            // var charRoutes = routes.GroupBy(r => r.CharactersInvolved).ToDictionary(g => g.Key, g => g.ToList());
            // foreach (var routeGrp in charRoutes)
            // {
            //     routeGrp.Value.Shuffle();
            // }
            //
            // foreach (ScenarioActivity activity in activities)
            // {
            //     for (int i = 0; i < activity.Routes.Count; i++)
            //     {
            //         activity.Routes[i] = charRoutes[activity.Routes[i].CharactersInvolved][0];
            //         charRoutes[activity.Routes[i].CharactersInvolved].RemoveAt(0);
            //     }
            // }

            activities.Shuffle();
            int a = 0;
            foreach (GroupSelectionItem groupSelection in groupSelectionItems)
            {
                for (int i = 0; i < groupSelection.Selection.Activities.Count; i++)
                {
                    if (groupSelection.Selection.Activities[i] is null || groupSelection.Selection.Activities[i].HaruhiPresent)
                    {
                        continue;
                    }
                    groupSelection.Selection.Activities[i] = activities[a++];
                }
                if (a >= activities.Count)
                {
                    break;
                }
            }

            a = 0;
            haruhiActivities.Shuffle();
            foreach (GroupSelectionItem groupSelection in groupSelectionItems)
            {
                for (int i = 0; i < groupSelection.Selection.Activities.Count; i++)
                {
                    if (groupSelection.Selection.Activities[i] is null || !groupSelection.Selection.Activities[i].HaruhiPresent)
                    {
                        continue;
                    }
                    groupSelection.Selection.Activities[i] = haruhiActivities[a++];
                    if (a >= haruhiActivities.Count)
                    {
                        break;
                    }
                }
                if (a >= haruhiActivities.Count)
                {
                    break;
                }
            }

            groupSelectionItems.Shuffle();

            for (int i = 0; i < project.Scenario.Selects.Count; i++)
            {
                project.Scenario.Selects[i] = groupSelectionItems[i].Selection;
            }
        }
        
        List<ScriptItem> scriptItems = project.Items
            .Where(i => i.Type == ItemDescription.ItemType.Script && !i.Name.StartsWith("CHS_")).Cast<ScriptItem>().ToList();
        var commandDict = scriptItems.Select(s => (s, s.GetScriptCommandTree(project, log)))
            .ToDictionary(s => s.s, s => s.Item2);
        List<PuzzleItem> puzzleItems = project.Items.Where(i => i.Type == ItemDescription.ItemType.Puzzle &&
                                                                i.GetReferencesTo(project).Count != 0 && !i.Name.Contains("SLG01"))
            .Cast<PuzzleItem>().ToList();
        if (topics)
        {
            Console.WriteLine("Randomizing topics...");
            
            List<(ScriptItem Script, ScriptSection Section, List<ScriptItemCommand> GetTopicCommands)> scriptCmdTopics =
                commandDict.SelectMany(s => s.Value.Select(sec =>
                    (s.Key, sec.Key, sec.Value.Where(c => c.Verb == EventFile.CommandVerb.TOPIC_GET).ToList()))).ToList();
            List<TopicItem> nonMainTopics = project.Items.Where(i =>
                i.Type == ItemDescription.ItemType.Topic && ((TopicItem)i)?.TopicEntry.Type != TopicType.Main).Cast<TopicItem>().ToList();
            List<TopicItem> mainTopics = project.Items.Where(i =>
                i.Type == ItemDescription.ItemType.Topic && ((TopicItem)i)?.TopicEntry.Type == TopicType.Main
                && !i.Name.Contains("30")).Cast<TopicItem>().ToList();
            List<int> episodeIndices = project.Scenario.Commands
                .Where(c => c.Verb == ScenarioCommand.ScenarioVerb.NEW_GAME)
                .Select(c => project.Scenario.Commands.IndexOf(c)).ToList();
            List<int> puzzleIndices = project.Scenario.Commands
                .Where(c => c.Verb == ScenarioCommand.ScenarioVerb.PUZZLE_PHASE)
                .Select(c => project.Scenario.Commands.IndexOf(c)).ToList();

            HashSet<ScriptItem> randomizedScripts = [];
            List<TopicItem> randoTopics = new(nonMainTopics);
            randoTopics.Shuffle();

            int rt = 0;
            foreach (TopicItem topic in nonMainTopics)
            {
                List<(ScriptItem Script, ScriptSection Section, List<ScriptItemCommand> Commands)> entriesWithThisTopic = scriptCmdTopics.Where(e => e.GetTopicCommands
                    .Any(c => ((TopicScriptParameter)c.Parameters[0]).TopicId == topic.TopicEntry.Id))
                    .Select(e => (e.Script, e.Section, e.GetTopicCommands
                        .Where(c => ((TopicScriptParameter)c.Parameters[0]).TopicId == topic.TopicEntry.Id).ToList())).ToList();
                foreach (var entry in entriesWithThisTopic)
                {
                    foreach (ScriptItemCommand getTopicCommand in entry.Commands)
                    {
                        entry.Script.Event.ScriptSections[entry.Script.Event.ScriptSections.IndexOf(entry.Section)]
                            .Objects[getTopicCommand.Index].Parameters[0] = randoTopics[rt].TopicEntry.Id;
                    }

                    randomizedScripts.Add(entry.Script);
                }

                rt++;
            }
            foreach (TopicItem topic in randoTopics)
            {
                (ScriptItem Script, ScriptSection Section, List<ScriptItemCommand> Commands) entry = scriptCmdTopics.Where(e => e.GetTopicCommands
                        .Any(c => ((TopicScriptParameter)c.Parameters[0]).TopicId == topic.TopicEntry.Id))
                    .Select(e => (e.Script, e.Section, e.GetTopicCommands
                        .Where(c => ((TopicScriptParameter)c.Parameters[0]).TopicId == topic.TopicEntry.Id).ToList())).First();
                
                int groupSelection = ((GroupSelectionItem?)project.Items.FirstOrDefault(i =>
                    i.Type == ItemDescription.ItemType.Group_Selection
                    && ((GroupSelectionItem)i).Selection.Activities.Where(a => a is not null).Select(a => a.Routes)
                    .Any(rs => rs.Any(r => r.ScriptIndex == entry.Script.Event.Index))))?.Index ?? 0;
                int scriptScenarioIdx = groupSelection < 1 ?
                    project.Scenario.Commands.FindIndex(c =>
                        c.Verb == ScenarioCommand.ScenarioVerb.LOAD_SCENE && c.Parameter == entry.Script.Event.Index)
                    : project.Scenario.Commands.FindIndex(c => c.Verb == ScenarioCommand.ScenarioVerb.ROUTE_SELECT
                                                               && c.Parameter == groupSelection);
                topic.TopicEntry.EpisodeGroup = (byte)(episodeIndices.FindLastIndex(e => e < scriptScenarioIdx) + 1);
                topic.TopicEntry.PuzzlePhaseGroup = (byte)puzzleIndices.FindIndex(e => e > scriptScenarioIdx);
            }
            
            List<TopicItem> randoMains = new(mainTopics);
            randoMains.Shuffle();

            rt = 0;
            foreach (TopicItem topic in mainTopics)
            {
                List<(ScriptItem Script, ScriptSection Section, List<ScriptItemCommand> Commands)> entriesWithThisTopic = scriptCmdTopics.Where(e => e.GetTopicCommands
                        .Any(c => ((TopicScriptParameter)c.Parameters[0]).TopicId == topic.TopicEntry.Id))
                    .Select(e => (e.Script, e.Section, e.GetTopicCommands
                        .Where(c => ((TopicScriptParameter)c.Parameters[0]).TopicId == topic.TopicEntry.Id).ToList())).ToList();
                // bool calculatedEpisode = false;
                foreach (var entry in entriesWithThisTopic)
                {
                    foreach (ScriptItemCommand getTopicCommand in entry.Commands)
                    {
                        entry.Script.Event.ScriptSections[entry.Script.Event.ScriptSections.IndexOf(entry.Section)]
                            .Objects[getTopicCommand.Index].Parameters[0] = randoMains[rt].TopicEntry.Id;
                    }
                }
                
                rt++;
            }
            foreach (TopicItem topic in randoMains)
            {
                (ScriptItem Script, ScriptSection Section, List<ScriptItemCommand> Commands) entry = scriptCmdTopics.Where(e => e.GetTopicCommands
                        .Any(c => ((TopicScriptParameter)c.Parameters[0]).TopicId == topic.TopicEntry.Id))
                    .Select(e => (e.Script, e.Section, e.GetTopicCommands
                        .Where(c => ((TopicScriptParameter)c.Parameters[0]).TopicId == topic.TopicEntry.Id).ToList())).FirstOrDefault();
                if (entry.Script is null)
                {
                    continue;
                }
                
                int groupSelection = ((GroupSelectionItem?)project.Items.FirstOrDefault(i =>
                    i.Type == ItemDescription.ItemType.Group_Selection
                    && ((GroupSelectionItem)i).Selection.Activities.Where(a => a is not null).Select(a => a.Routes)
                    .Any(rs => rs.Any(r => r.ScriptIndex == entry.Script.Event.Index))))?.Index ?? 0;
                int scriptScenarioIdx = groupSelection < 1 ?
                    project.Scenario.Commands.FindIndex(c =>
                        c.Verb == ScenarioCommand.ScenarioVerb.LOAD_SCENE && c.Parameter == entry.Script.Event.Index)
                    : project.Scenario.Commands.FindIndex(c => c.Verb == ScenarioCommand.ScenarioVerb.ROUTE_SELECT
                                                               && c.Parameter == groupSelection);
                topic.TopicEntry.EpisodeGroup = (byte)(episodeIndices.FindLastIndex(e => e < scriptScenarioIdx) + 1);
                topic.TopicEntry.PuzzlePhaseGroup = (byte)puzzleIndices.FindIndex(e => e > scriptScenarioIdx);
            }
            
            for (int i = 0; i < puzzleItems.Count; i++)
            {
                TopicItem[] epMainTopics = mainTopics.Where(top =>
                        top is not null && top.TopicEntry.PuzzlePhaseGroup == i + 1)
                    .DistinctBy(top => top.DisplayName).ToArray();
                int t = 0;
                for (int j = 0; j < puzzleItems[i].Puzzle.AssociatedTopics.Count && t < epMainTopics.Length; j++)
                {
                    puzzleItems[i].Puzzle.AssociatedTopics[j] = new(epMainTopics[t++].TopicEntry.Id, puzzleItems[i].Puzzle.AssociatedTopics[j].Unknown);
                    if (!puzzles)
                    {
                        SerialLoops.Lib.IO.WriteStringFile(Path.Combine("assets", "data", $"{puzzleItems[i].Puzzle.Index:X3}.s"), puzzleItems[i].Puzzle.GetSource(includes), project, log);
                    }
                }
            }
            
            foreach (ScriptItem script in randomizedScripts)
            {
                SerialLoops.Lib.IO.WriteStringFile(Path.Combine("assets", "events", $"{script.Event.Index:X3}.s"), script.Event.GetSource(includes), project, log);
            }
        }

        if (puzzles)
        {
            Console.WriteLine("Randomizing puzzles...");
            List<PuzzleItem> randoPuzzles = new(puzzleItems);
            randoPuzzles.Shuffle();
            for (int i = 0; i < puzzleItems.Count; i++)
            {
                puzzleItems[i].Puzzle.Settings.BaseTime = randoPuzzles[i].Puzzle.Settings.BaseTime;
            }
            if (crf)
            {
                List<int> mapIds = project.Items
                    .Where(i => i.Type == ItemDescription.ItemType.Map && ((MapItem)i).Map.Settings.SlgMode)
                    .Cast<MapItem>().Select(m => m.QmapIndex).ToList();
                mapIds.Shuffle();
                for (int i = 0; i < puzzleItems.Count; i++)
                {
                    puzzleItems[i].Puzzle.Settings.MapId = mapIds[i];
                }
            }
            else
            {
                randoPuzzles.Shuffle();
                for (int i = 0; i < puzzleItems.Count; i++)
                {
                    puzzleItems[i].Puzzle.Settings.MapId = randoPuzzles[i].Puzzle.Settings.MapId;
                }
            }
            randoPuzzles.Shuffle();
            for (int i = 0; i < puzzleItems.Count; i++)
            {
                puzzleItems[i].Puzzle.Settings.NumSingularities = randoPuzzles[i].Puzzle.Settings.NumSingularities;
            }
            randoPuzzles.Shuffle();
            for (int i = 0; i < puzzleItems.Count; i++)
            {
                puzzleItems[i].Puzzle.Settings.TargetNumber = randoPuzzles[i].Puzzle.Settings.TargetNumber;
            }
            randoPuzzles.Shuffle();
            for (int i = 0; i < puzzleItems.Count; i++)
            {
                puzzleItems[i].Puzzle.Settings.SingularityTexture = randoPuzzles[i].Puzzle.Settings.SingularityTexture;
                puzzleItems[i].Puzzle.Settings.SingularityLayout = randoPuzzles[i].Puzzle.Settings.SingularityLayout;
                puzzleItems[i].Puzzle.Settings.SingularityAnim1 = randoPuzzles[i].Puzzle.Settings.SingularityAnim1;
                puzzleItems[i].Puzzle.Settings.SingularityAnim2 = randoPuzzles[i].Puzzle.Settings.SingularityAnim2;
            }

            foreach (PuzzleItem puzzle in puzzleItems)
            {
                SerialLoops.Lib.IO.WriteStringFile(Path.Combine("assets", "data", $"{puzzle.Puzzle.Index:X3}.s"), puzzle.Puzzle.GetSource(includes), project, log);
            }
        }

        if (scenario)
        {
            // Delete first tutorial from the shuffled scenario for convenience since shuffling makes it unskippable
            project.Scenario.Commands.Remove(
                project.Scenario.Commands.First(c => c.Verb == ScenarioCommand.ScenarioVerb.PUZZLE_PHASE));
        }
        if (scenario || groupSelections)
        {
            SerialLoops.Lib.IO.WriteStringFile(
                Path.Combine("assets", "events", $"{project.Evt.GetFileByName("SCENARIOS").Index:X3}.s"),
                project.Scenario.GetSource(includes, log), project, log);
        }

        if (topics)
        {
            SerialLoops.Lib.IO.WriteStringFile(Path.Combine("assets", "events", $"{project.TopicFile.Index:X3}.s"),
                project.TopicFile.GetSource([]), project, log);
        }

        Console.WriteLine("Randomizing complete. Building ROM...");
        Build.BuildIterative(project, config, log, tracker);
        File.Move(Path.Combine(project.MainDirectory, $"{project.Name}.nds"), outputRom, overwrite: true);
        Console.WriteLine($"ROM output to '{outputRom}'. Enjoy!");
        Directory.Delete(project.MainDirectory, true);
    }
}