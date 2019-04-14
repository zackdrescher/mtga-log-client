﻿using System;
using System.Windows;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Threading;
using System.ComponentModel;
using System.Windows.Controls;
using Microsoft.Win32;
using System.Reflection;
using log4net;
using log4net.Core;

namespace mtga_log_client
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static readonly string REQUIRED_FILENAME = "output_log.txt";
        private static readonly string STARTUP_REGISTRY_CUSTOM_KEY = "17LandsMTGAClient";
        private static readonly string STARTUP_REGISTRY_LOCATION = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private static readonly string DOWNLOAD_URL = "https://github.com/rconroy293/mtga-log-client";
        private static readonly int MESSAGE_HISTORY = 150;

        private static readonly ILog log =LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private LogParser parser;
        private ApiClient client;
        BackgroundWorker worker;

        private bool isStarted = false;
        private string filePath;
        private string userToken;
        private bool runAtStartup;

        public MainWindow()
        {
            InitializeComponent();

            log4net.Config.XmlConfigurator.Configure();
            log.Info("        =============  Started Logging  =============        ");

            LoadSettings();
            UpdateStartupRegistryKey();
            SetupTrayMinimization();

            client = new ApiClient(LogMessage);

            if (!ValidateClientVersion()) return;

            if (ValidateUserInputs(false))
            {
                StartParser();
            }
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);

            if (!isStarted)
            {
                MessageBox.Show(
                    "Welcome to the 17Lands MTGA client. Please locate your log file and user token, then click 'Start Parsing' to begin.",
                    "Welcome",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (Properties.Settings.Default.do_not_ask_on_close)
            {
                base.OnClosing(e);
                return;
            }

            ExitConfirmation dialog = new ExitConfirmation();
            dialog.ShowDialog();

            switch (dialog.GetExitState())
            {
                case ExitConfirmation.ExitState.EXIT:
                    Properties.Settings.Default.do_not_ask_on_close = dialog.GetRemember();
                    Properties.Settings.Default.Save();
                    base.OnClosing(e);
                    break;
                case ExitConfirmation.ExitState.MINIMIZE:
                    e.Cancel = true;
                    this.Hide();
                    break;
                case ExitConfirmation.ExitState.CANCEL:
                    e.Cancel = true;
                    break;
            }
        }

        public void SetupTrayMinimization()
        {
            InitializeComponent();

            System.Windows.Forms.NotifyIcon ni = new System.Windows.Forms.NotifyIcon();

            ni.Icon = Properties.Resources.icon_white;
            ni.Visible = true;
            ni.DoubleClick += delegate (object sender, EventArgs args)
                {
                    this.Show();
                    this.WindowState = WindowState.Normal;
                };
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized) this.Hide();
            base.OnStateChanged(e);
        }

        private void LoadSettings()
        {
            userToken = Properties.Settings.Default.client_token;
            filePath = Properties.Settings.Default.mtga_log_filename;
            runAtStartup = Properties.Settings.Default.run_at_startup;

            RunAtStartupCheckbox.IsChecked = runAtStartup;
            LogFileTextBox.Text = filePath;
            ClientTokenTextBox.Text = userToken;
        }

        private void SaveSettings()
        {
            Properties.Settings.Default.client_token = userToken;
            Properties.Settings.Default.mtga_log_filename = filePath;
            Properties.Settings.Default.run_at_startup = runAtStartup;
            Properties.Settings.Default.Save();
        }

        private bool ValidateClientVersion()
        {
            var versionValidation = client.GetVersionValidation();
            if (versionValidation.is_supported)
            {
                return true;
            }

            MessageBox.Show(
                "This version of the client is no longer supported. Please update.",
                "Outdated Client",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            System.Diagnostics.Process.Start(DOWNLOAD_URL);
            Application.Current.Shutdown();
            return false;
        }

        private void StartParser()
        {
            if (worker != null && !worker.CancellationPending)
            {
                worker.CancelAsync();
            }
            isStarted = true;
            StartButton.IsEnabled = false;
            StartButton.Content = "Parsing";

            parser = new LogParser(client, userToken, filePath, LogMessage);

            worker = new BackgroundWorker();
            worker.DoWork += parser.ResumeParsing;
            worker.WorkerSupportsCancellation = true;
            worker.RunWorkerAsync();
        }

        private void StopParser()
        {
            if (!isStarted) return;
            LogMessage("Stopped parsing.", Level.Info);

            if (worker != null && !worker.CancellationPending)
            {
                worker.CancelAsync();
            }
            StartButton.IsEnabled = true;
            StartButton.Content = "Start Parsing";
            isStarted = false;
        }

        private bool ValidateLogFileInput(bool promptForUpdate)
        {
            if (File.Exists(LogFileTextBox.Text) && IsValidLogFile(LogFileTextBox.Text)) return true;

            if (promptForUpdate)
            {
                MessageBox.Show(
                    "You must choose a valid log file named " + REQUIRED_FILENAME,
                    "Choose Filename",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                filePath = ChooseLogFile();
                if (filePath != null)
                {
                    return true;
                }

                MessageBox.Show(
                    "You must enter a log file.",
                    "Choose Valid Log File",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            return false;
        }

        private bool ValidateTokenInput(bool promptForUpdate)
        {
            if (IsValidToken(ClientTokenTextBox.Text)) return true;

            if (promptForUpdate)
            {
                MessageBox.Show(
                    "You must enter a valid token from 17lands.com",
                    "Enter Valid Token",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            return false;
        }

        private bool ValidateUserInputs(bool promptForUpdate)
        {
            if (!ValidateLogFileInput(promptForUpdate)) return false;
            filePath = LogFileTextBox.Text;

            if (!ValidateTokenInput(promptForUpdate)) return false;
            userToken = ClientTokenTextBox.Text;

            return true;
        }

        private void RunAtStartupCheckbox_onClick(object sender, EventArgs e)
        {
            runAtStartup = RunAtStartupCheckbox.IsChecked.GetValueOrDefault(false);
            SaveSettings();
            UpdateStartupRegistryKey();
        }

        private void UpdateStartupRegistryKey()
        {
            var executorPath = Assembly.GetExecutingAssembly().Location;
            if (runAtStartup)
            {
                RegistryKey key = Registry.CurrentUser.OpenSubKey(STARTUP_REGISTRY_LOCATION, true);
                key.SetValue(STARTUP_REGISTRY_CUSTOM_KEY, executorPath);
            }
            else
            {
                RegistryKey key = Registry.CurrentUser.OpenSubKey(STARTUP_REGISTRY_LOCATION, true);
                key.DeleteValue(STARTUP_REGISTRY_CUSTOM_KEY, false);
            }
        }

        private void ClientTokenTextBox_onTextChanged(object sender, EventArgs e)
        {
            StopParser();
        }

        private bool IsValidToken(string clientToken)
        {
            var validationResponse = client.GetTokenValidation(clientToken);
            return validationResponse.is_valid;
        }

        private void ChooseFile_onClick(object sender, RoutedEventArgs e)
        {
            string newFilename = ChooseLogFile();
            if (newFilename != null)
            {
                LogFileTextBox.Text = newFilename;
                StopParser();
            }
        }

        private string ChooseLogFile()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Text files (*.txt)|*.txt";
            openFileDialog.InitialDirectory = filePath;

            if (openFileDialog.ShowDialog() == true)
            {
                if (IsValidLogFile(openFileDialog.FileName))
                {
                    LogFileTextBox.Text = openFileDialog.FileName;
                    return openFileDialog.FileName;
                }
                else
                {
                    MessageBox.Show(
                        "You must choose a file named " + REQUIRED_FILENAME,
                        "Bad Filename",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }

            return null;
        }

        private bool IsValidLogFile(string filename)
        {
            return filename.EndsWith("\\" + REQUIRED_FILENAME);
        }

        private void ValidateInputsApplyAndStart()
        {
            if (!ValidateUserInputs(true)) return;
            SaveSettings();
            StartParser();
        }

        private void OpenUserPageInBrowser(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("http://www.17lands.com/user");
        }

        private void OpenAccountPageInBrowser(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("http://www.17lands.com/account");
        }

        private void StartButton_onClick(object sender, EventArgs e)
        {
            if (!isStarted)
            {
                ValidateInputsApplyAndStart();
            }
        }

        private void LogMessage(string message, Level logLevel)
        {
            log.Logger.Log(null, logLevel, message, null);

            if (logLevel >= Level.Info)
            {
                Application.Current.Dispatcher.Invoke((Action)delegate {
                    var item = new ListBoxItem();
                    item.Content = message;
                    MessageListBox.Items.Insert(0, item);

                    while (MessageListBox.Items.Count > MESSAGE_HISTORY)
                    {
                        MessageListBox.Items.RemoveAt(MESSAGE_HISTORY);
                    }
                });
            }
        }

    }

    delegate void LogMessageFunction(string message, log4net.Core.Level logLevel);

    class LogParser
    {
        public const string CLIENT_VERSION = "0.1.1";
        public const string CLIENT_TYPE = "windows";

        private const int SLEEP_TIME = 750;
        private const int BUFFER_SIZE = 65536;
        private static readonly Regex LOG_START_REGEX = new Regex(
            "^\\[(UnityCrossThreadLogger|Client GRE)\\]([\\d:/ -]+(AM|PM)?)");
        private static readonly Regex LOG_START_REGEX_UNTIMED = new Regex(
            "^\\[(UnityCrossThreadLogger|Client GRE)\\]");
        private static readonly Regex LOG_START_REGEX_UNTIMED_2 = new Regex(
            "^\\(Filename:");
        private static readonly Regex JSON_DICT_REGEX = new Regex("\\{.+\\}");
        private static readonly Regex JSON_LIST_REGEX = new Regex("\\[.+\\]");

        private static readonly List<string> TIME_FORMATS = new List<string>() {
            "yyyy-MM-dd h:mm:ss tt",
            "yyyy-MM-dd HH:mm:ss",
            "M/d/yyyy h:mm:ss tt",
            "M/d/yyyy HH:mm:ss",
            "yyyy/MM/dd h:mm:ss tt",
            "yyyy/MM/dd HH:mm:ss",
            "dd/MM/yyyy HH:mm:ss",
            "dd.MM.yyyy HH:mm:ss"
        };

        private bool first = true;
        private long farthestReadPosition = 0;
        private List<string> buffer = new List<string>();
        private Nullable<DateTime> currentLogTime = null;
        private string currentUser = null;
        private readonly Dictionary<int, Dictionary<int, int>> objectsByOwner = new Dictionary<int, Dictionary<int, int>>();

        private const int ERROR_LINES_RECENCY = 10;
        private LinkedList<string> recentLines = new LinkedList<string>();
        private string lastBlob = "";
        private string currentDebugBlob = "";

        private readonly ApiClient apiClient;
        private readonly string apiToken;
        private readonly string filePath;
        private readonly LogMessageFunction messageFunction;

        public LogParser(ApiClient apiClient, string apiToken, string filePath, LogMessageFunction messageFunction)
        {
            this.apiClient = apiClient;
            this.apiToken = apiToken;
            this.filePath = filePath;
            this.messageFunction = messageFunction;
        }

        public void ResumeParsing(object sender, DoWorkEventArgs e)
        {
            LogMessage("Starting parsing of " + filePath, Level.Info);
            BackgroundWorker worker = sender as BackgroundWorker;

            while (!worker.CancellationPending)
            {
                ParseRemainderOfLog(worker);
                Thread.Sleep(SLEEP_TIME);
            }
        }

        public void ParseRemainderOfLog(BackgroundWorker worker) {
            try
            {
                using (FileStream filestream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BUFFER_SIZE))
                {
                    if (first || filestream.Length < farthestReadPosition)
                    {
                        filestream.Position = 0;
                        farthestReadPosition = filestream.Length;
                    }
                    else if (filestream.Length >= farthestReadPosition)
                    {
                        filestream.Position = farthestReadPosition;
                        farthestReadPosition = filestream.Length;
                    }
                    first = false;

                    using (StreamReader reader = new StreamReader(filestream))
                    {
                        while (!worker.CancellationPending)
                        {
                            string line = line = reader.ReadLine();
                            if (line == null)
                            {
                                break;
                            }
                            ProcessLine(line);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                LogMessage(String.Format("Error parsing log: {0}", e), Level.Error);
            }
        }

        private DateTime ParseDateTime(string dateString)
        {
            DateTime readDate;
            foreach (string format in TIME_FORMATS)
            {
                if (DateTime.TryParseExact(dateString, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out readDate))
                {
                    return readDate;
                }
            }
            return DateTime.Parse(dateString);
        }

        private void ProcessLine(string line)
        {
            if (recentLines.Count >= ERROR_LINES_RECENCY) recentLines.RemoveFirst();
            recentLines.AddLast(line);

            var match = LOG_START_REGEX_UNTIMED.Match(line);
            var match2 = LOG_START_REGEX_UNTIMED_2.Match(line);
            if (match.Success || match2.Success)
            {
                HandleCompleteLogEntry();
                var timedMatch = LOG_START_REGEX.Match(line);
                if (timedMatch.Success)
                {
                    currentLogTime = ParseDateTime(timedMatch.Groups[2].Value);
                }
            }
            else
            {
                buffer.Add(line);
            }
        }

        private void HandleCompleteLogEntry()
        {
            if (buffer.Count == 0)
            {
                return;
            }
            if (!currentLogTime.HasValue)
            {
                buffer.Clear();
                return;
            }

            var fullLog = String.Join("", buffer);
            currentDebugBlob = fullLog;
            try
            {
                HandleBlob(fullLog);
            }
            catch (Exception e)
            {
                LogMessage(String.Format("Error {0} while processing {1}", e, fullLog), Level.Error);
            }
            lastBlob = fullLog;

            buffer.Clear();
            currentLogTime = null;
        }

        private void HandleBlob(string fullLog)
        {
            var dictMatch = JSON_DICT_REGEX.Match(fullLog);
            if (!dictMatch.Success)
            {
                return;
            }

            var listMatch = JSON_LIST_REGEX.Match(fullLog);
            if (listMatch.Success && listMatch.Value.Length > dictMatch.Value.Length)
            {
                return;
            }

            var blob = JObject.Parse(dictMatch.Value);

            if (MaybeHandleLogin(blob)) return;
            if (MaybeHandleGameEnd(blob)) return;
            if (MaybeHandleDraftLog(blob)) return;
            if (MaybeHandleDraftPick(blob)) return;
            if (MaybeHandleDeckSubmission(blob)) return;
            if (MaybeHandleDeckSubmissionV3(blob)) return;
            if (MaybeHandleEventCompletion(blob)) return;
            if (MaybeHandleGreToClientMessages(blob)) return;
        }

        private bool MaybeHandleLogin(JObject blob)
        {
            JToken token;
            if (!blob.TryGetValue("params", out token)) return false;
            if (!token.Value<JObject>().TryGetValue("messageName", out token)) return false;
            if (!token.Value<String>().Equals("Client.Connected")) return false;

            try
            {
                var payload = blob["params"]["payloadObject"];

                currentUser = payload["playerId"].Value<String>();
                var screenName = payload["screenName"].Value<String>();

                MTGAAccount account = new MTGAAccount();
                account.token = apiToken;
                account.client_version = CLIENT_VERSION;
                account.player_id = currentUser;

                account.screen_name = screenName;
                apiClient.PostMTGAAccount(account);

                return true;
            }
            catch (Exception e)
            {
                LogMessage(String.Format("Error {0} parsing login from {1}", e, blob), Level.Warn);
                return false;
            }
        }

        private bool MaybeHandleGameEnd(JObject blob)
        {
            JToken token;
            if (!blob.TryGetValue("params", out token)) return false;
            if (!token.Value<JObject>().TryGetValue("messageName", out token)) return false;
            if (!token.Value<String>().Equals("DuelScene.GameStop")) return false;

            try
            {
                var payload = blob["params"]["payloadObject"];

                var opponentId = payload["seatId"].Value<int>() == 1 ? 2 : 1;
                var opponentCardIds = new List<int>();
                if (objectsByOwner.ContainsKey(opponentId))
                {
                    foreach(KeyValuePair<int, int> entry in objectsByOwner[opponentId])
                    {
                        opponentCardIds.Add(entry.Value);
                    }
                }
                objectsByOwner.Clear();

                var mulligans = new List<List<int>>();
                foreach (JArray hand in payload["mulliganedHands"].Value<JArray>())
                {
                    var mulliganHand = new List<int>();
                    foreach (JObject card in hand)
                    {
                        mulliganHand.Add(card["grpId"].Value<int>());
                    }
                    mulligans.Add(mulliganHand);
                }

                Game game = new Game();
                game.token = apiToken;
                game.client_version = CLIENT_VERSION;
                game.player_id = currentUser;
                game.time = GetDatetimeString(currentLogTime.Value);

                game.event_name = payload["eventId"].Value<string>();
                game.match_id = payload["matchId"].Value<string>();
                game.on_play = payload["teamId"].Value<int>() == payload["startingTeamId"].Value<int>();
                game.won = payload["teamId"].Value<int>() == payload["winningTeamId"].Value<int>();
                game.game_end_reason = payload["winningReason"].Value<string>();
                game.mulligans = mulligans;
                game.turns = payload["turnCount"].Value<int>();
                game.duration = payload["secondsCount"].Value<int>();
                game.opponent_card_ids = opponentCardIds;

                apiClient.PostGame(game);

                return true;
            }
            catch (Exception e)
            {
                LogMessage(String.Format("Error {0} parsing game result from {1}", e, blob), Level.Warn);
                return false;
            }
        }

        private bool MaybeHandleDraftLog(JObject blob)
        {
            if (!blob.ContainsKey("draftStatus")) return false;
            if (!"Draft.PickNext".Equals(blob["draftStatus"].Value<String>())) return false;

            try
            {
                Pack pack = new Pack();
                pack.token = apiToken;
                pack.client_version = CLIENT_VERSION;
                pack.player_id = currentUser;
                pack.time = GetDatetimeString(currentLogTime.Value);

                var cardIds = new List<int>();
                foreach (JToken cardString in blob["draftPack"].Value<JArray>())
                {
                    cardIds.Add(int.Parse(cardString.Value<String>()));
                }

                pack.event_name = blob["eventName"].Value<String>();
                pack.pack_number = blob["packNumber"].Value<int>();
                pack.pick_number = blob["pickNumber"].Value<int>();
                pack.card_ids = cardIds;

                apiClient.PostPack(pack);
                return true;
            }
            catch (Exception e)
            {
                LogMessage(String.Format("Error {0} parsing draft pack from {1}", e, blob), Level.Warn);
                return false;
            }
        }

        private bool MaybeHandleDraftPick(JObject blob)
        {
            if (!blob.ContainsKey("method")) return false;
            if (!"Draft.MakePick".Equals(blob["method"].Value<String>())) return false;

            try
            {
                var parameters = blob["params"].Value<JObject>();
                var draftIdComponents = parameters["draftId"].Value<String>().Split(':');

                Pick pick = new Pick();
                pick.token = apiToken;
                pick.client_version = CLIENT_VERSION;
                pick.player_id = currentUser;
                pick.time = GetDatetimeString(currentLogTime.Value);

                pick.event_name = draftIdComponents[1];
                pick.pack_number = parameters["packNumber"].Value<int>();
                pick.pick_number = parameters["pickNumber"].Value<int>();
                pick.card_id = parameters["cardId"].Value<int>();

                apiClient.PostPick(pick);

                return true;
            }
            catch (Exception e)
            {
                LogMessage(String.Format("Error {0} parsing draft pick from {1}", e, blob), Level.Warn);
                return false;
            }
        }

        private bool MaybeHandleDeckSubmission(JObject blob)
        {
            if (!blob.ContainsKey("method")) return false;
            if (!"Event.DeckSubmit".Equals(blob["method"].Value<String>())) return false;

            try
            {
                objectsByOwner.Clear();

                Deck deck = new Deck();
                deck.token = apiToken;
                deck.client_version = CLIENT_VERSION;
                deck.player_id = currentUser;
                deck.time = GetDatetimeString(currentLogTime.Value);

                var parameters = blob["params"].Value<JObject>();
                var deckInfo = JObject.Parse(parameters["deck"].Value<String>());

                var maindeckCardIds = GetCardIdsFromDeck(deckInfo["mainDeck"].Value<JArray>());
                var sideboardCardIds = GetCardIdsFromDeck(deckInfo["sideboard"].Value<JArray>());

                deck.event_name = parameters["eventName"].Value<String>();
                deck.maindeck_card_ids = maindeckCardIds;
                deck.sideboard_card_ids = sideboardCardIds;
                deck.is_during_match = false;

                apiClient.PostDeck(deck);
                return true;
            }
            catch (Exception e)
            {
                LogMessage(String.Format("Error {0} parsing deck submission from {1}", e, blob), Level.Warn);
                return false;
            }
        }

        private bool MaybeHandleDeckSubmissionV3(JObject blob)
        {
            if (!blob.ContainsKey("method")) return false;
            if (!"Event.DeckSubmitV3".Equals(blob["method"].Value<String>())) return false;

            try
            {
                objectsByOwner.Clear();

                Deck deck = new Deck();
                deck.token = apiToken;
                deck.client_version = CLIENT_VERSION;
                deck.player_id = currentUser;
                deck.time = GetDatetimeString(currentLogTime.Value);

                var parameters = blob["params"].Value<JObject>();
                var deckInfo = JObject.Parse(parameters["deck"].Value<String>());

                var maindeckCardIds = GetCardIdsFromDecklistV3(deckInfo["mainDeck"].Value<JArray>());
                var sideboardCardIds = GetCardIdsFromDecklistV3(deckInfo["sideboard"].Value<JArray>());

                deck.event_name = parameters["eventName"].Value<String>();
                deck.maindeck_card_ids = maindeckCardIds;
                deck.sideboard_card_ids = sideboardCardIds;
                deck.is_during_match = false;

                apiClient.PostDeck(deck);
                return true;
            }
            catch (Exception e)
            {
                LogMessage(String.Format("Error {0} parsing v3 deck submission from {1}", e, blob), Level.Warn);
                return false;
            }
        }

        private bool MaybeHandleEventCompletion(JObject blob)
        {
            if (!blob.ContainsKey("CurrentEventState")) return false;
            if (!"DoneWithMatches".Equals(blob["CurrentEventState"].Value<String>())) return false;

            try
            {
                Event event_ = new Event();
                event_.token = apiToken;
                event_.client_version = CLIENT_VERSION;
                event_.player_id = currentUser;
                event_.time = GetDatetimeString(currentLogTime.Value);

                event_.event_name = blob["InternalEventName"].Value<String>();
                event_.entry_fee = blob["ModuleInstanceData"]["HasPaidEntry"].Value<String>();
                event_.wins = blob["ModuleInstanceData"]["WinLossGate"]["CurrentWins"].Value<int>();
                event_.losses = blob["ModuleInstanceData"]["WinLossGate"]["CurrentLosses"].Value<int>();

                apiClient.PostEvent(event_);
                return true;
            }
            catch (Exception e)
            {
                LogMessage(String.Format("Error {0} parsing event completion from {1}", e, blob), Level.Warn);
                return false;
            }
        }

        private bool MaybeHandleGreMessage_DeckSubmission(JToken blob)
        {
            if (!"GREMessageType_SubmitDeckReq".Equals(blob["type"].Value<string>())) return false;

            try
            {
                objectsByOwner.Clear();

                Deck deck = new Deck();
                deck.token = apiToken;
                deck.client_version = CLIENT_VERSION;
                deck.player_id = currentUser;
                deck.time = GetDatetimeString(currentLogTime.Value);

                deck.event_name = null;
                deck.maindeck_card_ids = JArrayToIntList(blob["submitDeckReq"]["deck"]["deckCards"].Value<JArray>());
                deck.sideboard_card_ids = JArrayToIntList(blob["submitDeckReq"]["deck"]["sideboardCards"].Value<JArray>());
                deck.is_during_match = true;

                apiClient.PostDeck(deck);
                return true;
            }
            catch (Exception e)
            {
                LogMessage(String.Format("Error {0} parsing GRE deck submission from {1}", e, blob), Level.Warn);
                return false;
            }
        }

        private bool MaybeHandleGreMessage_GameState(JToken blob)
        {
            if (!"GREMessageType_GameStateMessage".Equals(blob["type"].Value<string>())) return false;

            try
            {
                var gameStateMessage = blob["gameStateMessage"].Value<JObject>();
                if (!gameStateMessage.ContainsKey("gameObjects")) return true;
                var gameObjects = gameStateMessage["gameObjects"].Value<JArray>();

                foreach (JToken gameObject in gameObjects)
                {
                    if (!"GameObjectType_Card".Equals(gameObject["type"].Value<string>())) continue;

                    var owner = gameObject["ownerSeatId"].Value<int>();
                    var instanceId = gameObject["instanceId"].Value<int>();
                    var cardId = gameObject["overlayGrpId"].Value<int>();

                    if (!objectsByOwner.ContainsKey(owner))
                    {
                        objectsByOwner.Add(owner, new Dictionary<int, int>());
                    }
                    objectsByOwner[owner][instanceId] = cardId;
                }
                return true;
            }
            catch (Exception e)
            {
                LogMessage(String.Format("Error {0} parsing GRE deck submission from {1}", e, blob), Level.Warn);
                return false;
            }
        }

        private bool MaybeHandleGreToClientMessages(JObject blob)
        {
            if (!blob.ContainsKey("greToClientEvent")) return false;
            if (!blob["greToClientEvent"].Value<JObject>().ContainsKey("greToClientMessages")) return false;

            try
            {
                foreach (JToken message in blob["greToClientEvent"]["greToClientMessages"])
                {
                    if (MaybeHandleGreMessage_DeckSubmission(message)) continue;
                    if (MaybeHandleGreMessage_GameState(message)) continue;
                }
                return true;
            }
            catch (Exception e)
            {
                LogMessage(String.Format("Error {0} parsing event completion from {1}", e, blob), Level.Warn);
                return false;
            }
        }

        private void LogMessage(string message, Level logLevel)
        {
            messageFunction(message, logLevel);
            if (logLevel != Level.Info)
            {
                messageFunction(String.Format("Current blob: {0}", currentDebugBlob), Level.Debug);
                messageFunction(String.Format("Previous blob: {0}", lastBlob), Level.Debug);
                messageFunction("Recent lines:", Level.Debug);
                foreach (string line in recentLines)
                {
                    messageFunction(line, Level.Debug);
                }
            }
        }

        private string GetDatetimeString(DateTime value)
        {
            return value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private List<int> JArrayToIntList(JArray arr)
        {
            var output = new List<int>();
            foreach (JToken token in arr)
            {
                output.Add(token.Value<int>());
            }
            return output;
        }

        private List<int> GetCardIdsFromDeck(JArray decklist)
        {
            var cardIds = new List<int>();
            foreach (JObject cardInfo in decklist)
            {
                int cardId;
                if (cardInfo.ContainsKey("id"))
                {
                    cardId = cardInfo["id"].Value<int>();
                }
                else
                {
                    cardId = cardInfo["Id"].Value<int>();
                }

                for (int i = 0; i < cardInfo["Quantity"].Value<int>(); i++)
                {
                    cardIds.Add(cardId);
                }
            }
            return cardIds;
        }

        private List<int> GetCardIdsFromDecklistV3(JArray decklist)
        {
            var cardIds = new List<int>();
            for (int i = 0; i < decklist.Count / 2; i++)
            {
                var cardId = decklist[2 * i].Value<int>();
                var count = decklist[2 * i + 1].Value<int>();
                for (int j = 0; j < count; j++)
                {
                    cardIds.Add(cardId);
                }
            }
            return cardIds;
        }

    }

    class ApiClient
    {
        private const string API_BASE_URL = "https://www.17lands.com";
        private const string ENDPOINT_ACCOUNT = "api/account";
        private const string ENDPOINT_DECK = "deck";
        private const string ENDPOINT_EVENT = "event";
        private const string ENDPOINT_GAME = "game";
        private const string ENDPOINT_PACK = "pack";
        private const string ENDPOINT_PICK = "pick";
        private const string ENDPOINT_CLIENT_VERSION_VALIDATION = "api/version_validation";
        private const string ENDPOINT_TOKEN_VERSION_VALIDATION = "api/token_validation";

        private static readonly DataContractJsonSerializer SERIALIZER_MTGA_ACCOUNT = new DataContractJsonSerializer(typeof(MTGAAccount));
        private static readonly DataContractJsonSerializer SERIALIZER_PACK = new DataContractJsonSerializer(typeof(Pack));
        private static readonly DataContractJsonSerializer SERIALIZER_PICK = new DataContractJsonSerializer(typeof(Pick));
        private static readonly DataContractJsonSerializer SERIALIZER_DECK = new DataContractJsonSerializer(typeof(Deck));
        private static readonly DataContractJsonSerializer SERIALIZER_GAME = new DataContractJsonSerializer(typeof(Game));
        private static readonly DataContractJsonSerializer SERIALIZER_EVENT = new DataContractJsonSerializer(typeof(Event));

        private HttpClient client;
        private readonly LogMessageFunction messageFunction;

        [DataContract]
        public class VersionValidationResponse
        {
            [DataMember]
            internal bool is_supported;
            [DataMember]
            internal string latest_version;
        }

        [DataContract]
        public class TokenValidationResponse
        {
            [DataMember]
            internal bool is_valid;
        }

        public ApiClient(LogMessageFunction messageFunction)
        {
            this.messageFunction = messageFunction;
            InitializeClient();
        }

        public void InitializeClient()
        {
            client = new HttpClient();
            client.BaseAddress = new Uri(API_BASE_URL);
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public void StopClient()
        {
            client.Dispose();
        }

        private Stream GetJson(string endpoint)
        {
            HttpResponseMessage response = client.GetAsync(endpoint).Result;
            if (response.IsSuccessStatusCode)
            {
                return response.Content.ReadAsStreamAsync().Result;
            }
            else
            {
                LogMessage(String.Format("Got error response {0} ({1})", (int) response.StatusCode, response.ReasonPhrase), Level.Warn);
                return null;
            }
        }

        private void PostJson(string endpoint, String blob)
        {
            LogMessage(String.Format("Posting {0} of {1}", endpoint, blob), Level.Info);
            var content = new StringContent(blob, Encoding.UTF8, "application/json");
            var response = client.PostAsync(endpoint, content).Result;
            if (!response.IsSuccessStatusCode)
            {
                LogMessage(String.Format("Got error response {0} ({1})", (int)response.StatusCode, response.ReasonPhrase), Level.Warn);
            }
        }

        public VersionValidationResponse GetVersionValidation()
        {
            var jsonResponse = GetJson(ENDPOINT_CLIENT_VERSION_VALIDATION + "?client=" + LogParser.CLIENT_TYPE + "&version=" + LogParser.CLIENT_VERSION);
            if (jsonResponse == null)
            {
                return null;
            }
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(VersionValidationResponse));
            return ((VersionValidationResponse)serializer.ReadObject(jsonResponse));
        }

        public TokenValidationResponse GetTokenValidation(string token)
        {
            var jsonResponse = GetJson(ENDPOINT_TOKEN_VERSION_VALIDATION + "?token=" + token);
            if (jsonResponse == null)
            {
                return null;
            }
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(TokenValidationResponse));
            return ((TokenValidationResponse)serializer.ReadObject(jsonResponse));
        }

        public void PostMTGAAccount(MTGAAccount account)
        {
            MemoryStream stream = new MemoryStream();
            SERIALIZER_MTGA_ACCOUNT.WriteObject(stream, account);
            string jsonString = Encoding.UTF8.GetString(stream.ToArray());
            PostJson(ENDPOINT_ACCOUNT, jsonString);
        }

        public void PostPack(Pack pack)
        {
            MemoryStream stream = new MemoryStream();
            SERIALIZER_PACK.WriteObject(stream, pack);
            string jsonString = Encoding.UTF8.GetString(stream.ToArray());
            PostJson(ENDPOINT_PACK, jsonString);
        }

        public void PostPick(Pick pick)
        {
            MemoryStream stream = new MemoryStream();
            SERIALIZER_PICK.WriteObject(stream, pick);
            string jsonString = Encoding.UTF8.GetString(stream.ToArray());
            PostJson(ENDPOINT_PICK, jsonString);
        }

        public void PostDeck(Deck deck)
        {
            MemoryStream stream = new MemoryStream();
            SERIALIZER_DECK.WriteObject(stream, deck);
            string jsonString = Encoding.UTF8.GetString(stream.ToArray());
            PostJson(ENDPOINT_DECK, jsonString);
        }

        public void PostGame(Game game)
        {
            MemoryStream stream = new MemoryStream();
            SERIALIZER_GAME.WriteObject(stream, game);
            string jsonString = Encoding.UTF8.GetString(stream.ToArray());
            PostJson(ENDPOINT_GAME, jsonString);
        }

        public void PostEvent(Event event_)
        {
            MemoryStream stream = new MemoryStream();
            SERIALIZER_EVENT.WriteObject(stream, event_);
            string jsonString = Encoding.UTF8.GetString(stream.ToArray());
            PostJson(ENDPOINT_EVENT, jsonString);
        }

        private void LogMessage(string message, Level logLevel)
        {
            messageFunction(message, logLevel);
        }
    }

    [DataContract]
    internal class MTGAAccount
    {
        [DataMember]
        internal string client_version;
        [DataMember]
        internal string token;
        [DataMember]
        internal string player_id;
        [DataMember]
        internal string screen_name;
    }
    [DataContract]
    internal class Pack
    {
        [DataMember]
        internal string client_version;
        [DataMember]
        internal string token;
        [DataMember]
        internal string player_id;
        [DataMember]
        internal string event_name;
        [DataMember]
        internal string time;
        [DataMember]
        internal int pack_number;
        [DataMember]
        internal int pick_number;
        [DataMember]
        internal List<int> card_ids;
    }
    [DataContract]
    internal class Pick
    {
        [DataMember]
        internal string client_version;
        [DataMember]
        internal string token;
        [DataMember]
        internal string player_id;
        [DataMember]
        internal string event_name;
        [DataMember]
        internal string time;
        [DataMember]
        internal int pack_number;
        [DataMember]
        internal int pick_number;
        [DataMember]
        internal int card_id;
    }
    [DataContract]
    internal class Deck
    {
        [DataMember]
        internal string client_version;
        [DataMember]
        internal string token;
        [DataMember]
        internal string player_id;
        [DataMember]
        internal string time;
        [DataMember]
        internal string event_name;
        [DataMember]
        internal List<int> maindeck_card_ids;
        [DataMember]
        internal List<int> sideboard_card_ids;
        [DataMember]
        internal bool is_during_match;
    }
    [DataContract]
    internal class Game
    {
        [DataMember]
        internal string client_version;
        [DataMember]
        internal string token;
        [DataMember]
        internal string player_id;
        [DataMember]
        internal string event_name;
        [DataMember]
        internal string match_id;
        [DataMember]
        internal string time;
        [DataMember]
        internal bool on_play;
        [DataMember]
        internal bool won;
        [DataMember]
        internal string game_end_reason;
        [DataMember]
        internal List<List<int>> mulligans;
        [DataMember]
        internal int turns;
        [DataMember]
        internal int duration;
        [DataMember]
        internal List<int> opponent_card_ids;
    }
    [DataContract]
    internal class Event
    {
        [DataMember]
        internal string client_version;
        [DataMember]
        internal string token;
        [DataMember]
        internal string player_id;
        [DataMember]
        internal string event_name;
        [DataMember]
        internal string time;
        [DataMember]
        internal string entry_fee;
        [DataMember]
        internal int wins;
        [DataMember]
        internal int losses;
    }
}
