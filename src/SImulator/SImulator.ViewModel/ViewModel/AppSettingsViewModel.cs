﻿using SIEngine;
using SImulator.ViewModel.Model;
using SImulator.ViewModel.PlatformSpecific;
using SIUI.ViewModel;
using SIUI.ViewModel.Core;
using System.Windows.Input;
using System.Xml.Serialization;

namespace SImulator.ViewModel
{
    public sealed class AppSettingsViewModel
    {
        public AppSettings Model { get; private set; }
        public SettingsViewModel SIUISettings { get; private set; }

        public GameModes[] Modes { get; } = new GameModes[] { GameModes.Tv, GameModes.Sport };

        [XmlIgnore]
        public ICommand Reset { get; private set; }

        public AppSettingsViewModel(AppSettings settings)
        {
            Model = settings;
            SIUISettings = new SettingsViewModel(settings.SIUISettings);

            Reset = new SimpleCommand(Reset_Executed);
        }

        internal void Reset_Executed(object arg)
        {
            var defaultSettings = new AppSettings();
            PlatformManager.Instance.InitSettings(defaultSettings);

            var defaultUISettings = new Settings();
            var currentSettings = SIUISettings;

            var design = arg == null || arg.ToString() == "1";
            var rules = arg == null || arg.ToString() == "2";
            var buttons = arg == null || arg.ToString() == "4";

            if (design)
            {
                currentSettings.QuestionLineSpacing = defaultUISettings.QuestionLineSpacing;
                currentSettings.Model.TableColorString = defaultUISettings.TableColorString;
                currentSettings.Model.TableBackColorString = defaultUISettings.TableBackColorString;
                currentSettings.TableFontFamily = defaultUISettings.TableFontFamily;
                Model.VideoUrl = defaultSettings.VideoUrl;
                currentSettings.Model.LogoUri = defaultUISettings.LogoUri;
                Model.ShowRight = defaultSettings.ShowRight;
                Model.ShowTextNoFalstart = defaultSettings.ShowTextNoFalstart;

                currentSettings.Model.ShowScore = defaultUISettings.ShowScore;
            }

            if (rules)
            {
                Model.BlockingTime = defaultSettings.BlockingTime;
                Model.DropStatsOnBack = defaultSettings.DropStatsOnBack;
                Model.FalseStart = defaultSettings.FalseStart;
                Model.HttpPort = defaultSettings.HttpPort;
                Model.IsRemoteControlAllowed = defaultSettings.IsRemoteControlAllowed;
                Model.EndQuestionOnRightAnswer = defaultSettings.EndQuestionOnRightAnswer;
                Model.RoundTime = defaultSettings.RoundTime;
                Model.SignalsAfterTimer = defaultSettings.SignalsAfterTimer;
                Model.ThinkingTime = defaultSettings.ThinkingTime;
                Model.UsePlayersKeys = defaultSettings.UsePlayersKeys;
                Model.PlayersView = defaultSettings.PlayersView;
                Model.SaveLogs = defaultSettings.SaveLogs;
                Model.AutomaticGame = defaultSettings.AutomaticGame;
                Model.SubstractOnWrong = defaultSettings.SubstractOnWrong;
                Model.PlaySpecials = defaultSettings.PlaySpecials;
                Model.FalseStartMultimedia = defaultSettings.FalseStartMultimedia;
                Model.GameMode = defaultSettings.GameMode;
            }

            if (buttons)
            {
                currentSettings.Model.KeyboardControl = defaultUISettings.KeyboardControl;
            }
        }
    }
}
