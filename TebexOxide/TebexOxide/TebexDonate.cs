using System;
using System.Collections.Generic;

using Newtonsoft.Json.Linq;

using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{

    [Info("Tebex Donate", "Tebex", "1.3.0")]
    [Description("Official Plugin for the Tebex Server Monetization Platform.")]
    public class TebexDonate : CovalencePlugin
    {

        #region Classes

        private struct Command
        {

            public int Id { get; }
            public bool Online { get; }
            public string PlayerId { get; }
            public string CommandString { get; }

            public Command(int id, bool online, string playerId, string commandString)
            {
                Id = id;
                Online = online;
                PlayerId = playerId;
                CommandString = commandString;
            }

        }

        #endregion

        #region Constants and Variables

        #region API

        private static readonly string BASE_URL = "https://plugin.buycraft.net";
        private string storeUrl;
        private bool validated;

        #endregion

        #region Configuration

        private bool buyEnabled;
        private string buyCommand;
        private bool debugLogActions;
        private bool debugLogResponseErrors;
        private bool debugLogStackTraces;
        private string secretKey;

        #endregion

        private Timer checkTimer;
        private Timer validationTimer;

        #endregion

        #region Initialisation

        protected override void LoadDefaultConfig()
        {
            Config["Buy Command", "Enabled"] = buyEnabled = GetConfig("Buy Command", "Enabled", true);
            Config["Buy Command", "Alias"] = buyCommand = GetConfig("Buy Command", "Alias", "buy");
            Config["Debug", "Log Actions"] = debugLogActions = GetConfig("Debug", "Log Actions", true);
            Config["Debug", "Log Stack Traces"] = debugLogStackTraces = GetConfig("Debug", "Log Stack Traces", true);
            Config["Debug", "Log Response Errors"] = debugLogResponseErrors = GetConfig("Debug", "Log Response Errors", true);
            Config["Secret key of your shop (do not tell it anyone)"] = secretKey = GetConfig("Secret key of your shop (do not tell it anyone)", "");

            SaveConfig();
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["BuyCommand"] = "You can buy packages from our store at {url}"
            }, this);
        }

        private void OnServerInitialized()
        {
            LoadDefaultConfig();
            LoadDefaultMessages();

            if (buyEnabled)
                AddCovalenceCommand(buyCommand, "BuyCommand");

            validated = false;
            StartValidationTimer();
        }

        private void Unload()
        {
            if (checkTimer != null && !checkTimer.Destroyed)
                checkTimer.Destroy();

            if (validationTimer != null && !validationTimer.Destroyed)
                validationTimer.Destroy();
        }

        #endregion

        #region Commands

        #region Player Commands

        private void BuyCommand(IPlayer player, string command, string[] args)
        {
            if (player.IsServer)
            {
                Puts("You cannot use this command from the console window!");
                return;
            }

            player.Message(lang.GetMessage("BuyCommand", this, player.Id).Replace("{url}", storeUrl));
        }

        #endregion

        #region Server Commands

        [Command("tebex:info")]
        private void TebexInfoCommand(IPlayer player, string command, string[] args)
        {
            if (!player.IsServer)
                return;

            FetchStoreInformation(true, secretKey);
        }

        [Command("tebex:secret")]
        private void TebexSecretCommand(IPlayer player, string command, string[] args)
        {
            if (!player.IsServer)
                return;

            if (args.Length != 1)
            {
                PrintWarning("Usage: tebex:secret <secret>");
                return;
            }

            FetchStoreInformation(true, args[0]);
        }

        #endregion

        #endregion

        #region Web Request Methods

        private void CheckCommandQueue(bool first)
        {
            if (validated)
            {
                if (first && validationTimer != null && !validationTimer.Destroyed)
                    validationTimer.Destroy();
            }
            else
                return;

            if (debugLogActions)
                PrintWarning("Attempting to process commands in the queue...");

            webrequest.Enqueue($"{BASE_URL}/queue", "", (code, response) =>
            {
                float secondsUntilNextCheck = 225f;

                if (response != null)
                {
                    try
                    {
                        var jObject = JObject.Parse(response);

                        switch (code)
                        {
                            case 200:
                                break;
                            case 403:
                                StartValidationTimer(60f);
                                goto default;
                            default:
                                PrintWarning($"An error occurred whilst checking the command queue: {jObject["error_message"].ToString()}");
                                return;
                        }

                        if ((bool) jObject["meta"]["execute_offline"])
                        {
                            if (debugLogActions)
                                PrintWarning("Processing offline commands...");

                            ProcessOfflineCommands();
                        }

                        JArray jObjectPlayers = (JArray) jObject["players"];
                        secondsUntilNextCheck = (int) jObject["meta"]["next_check"] / 4;

                        if (jObjectPlayers.Count > 0)
                        {
                            int processed = 0;

                            foreach (var player in jObjectPlayers)
                            {
                                IPlayer target = players.FindPlayerById(player["uuid"].ToString());

                                if (target == null || !target.IsConnected)
                                    continue;

                                if (debugLogActions)
                                    PrintWarning($"Processing online commands for {target.Id}...");

                                ProcessOnlineCommands(target, player["id"].ToString());
                                processed++;
                            }

                            if (processed == 0 && debugLogActions)
                                PrintWarning("There are no online commands that need to be processed!");
                        }
                        else if (debugLogActions)
                            PrintWarning("There are no online commands that need to be processed!");
                    }
                    catch (Exception e)
                    {
                        PrintError($"An exception was thrown whilst checking the command queue: {e.Message}");

                        if (debugLogResponseErrors)
                            PrintError($"Associated response: {response}");

                        if (debugLogStackTraces)
                            PrintError(e.StackTrace);
                    }

                    checkTimer = timer.In(secondsUntilNextCheck, () => CheckCommandQueue(false));
                    return;
                }

                checkTimer = timer.In(secondsUntilNextCheck, () => CheckCommandQueue(false));
                PrintError($"An unhandled error occurred whilst checking the command queue (response code: {code}).");
            }, this, RequestMethod.GET, AddToHeaders(secretKey), 3000f);
        }

        private void DeleteCommands(List<Command> commands)
        {
            if (commands.Count < 1)
                return;

            string commandIds = $"ids[]={commands[0].Id}";

            for (int i = 0; i < commands.Count; i++)
            {
                Command command = commands[i];

                if (debugLogActions)
                    PrintWarning($"Queueing command {command.Id} for deletion: {command.CommandString}");

                if (i == 0)
                    continue;

                commandIds += $"&ids[]={command.Id}";
            }

            webrequest.Enqueue($"{BASE_URL}/queue?{commandIds}", "", (code, response) =>
            {
                switch (code)
                {
                    case 204:
                        if (debugLogActions)
                            PrintWarning("Successfully deleted all executed commands.");

                        return;
                    case 403:
                        StartValidationTimer(60f);
                        goto default;
                    default:
                        if (response != null)
                        {
                            try
                            {
                                var jObject = JObject.Parse(response);

                                PrintWarning($"An error occurred whilst deleting executed commands: {jObject["error_message"].ToString()}");
                                return;
                            }
                            catch (Exception e)
                            {
                                PrintError($"An exception was thrown whilst deleting executed commands: {e.Message}");

                                if (debugLogResponseErrors)
                                    PrintError($"Associated response: {response}");

                                if (debugLogStackTraces)
                                    PrintError(e.StackTrace);

                                return;
                            }
                        }

                        break;
                }

                PrintError($"An unhandled error occurred whilst deleting executed commands (response code: {code}).");
            }, this, RequestMethod.DELETE, AddToHeaders(secretKey), 3000f);
        }

        private void FetchStoreInformation(bool command, string secretKey)
        {
            if (!command && !validated)
                validationTimer = timer.In(60f, () => FetchStoreInformation(false, this.secretKey));

            if (secretKey.Equals(""))
            {
                PrintWarning("You have not yet set your store's secret key in the configuration file!");
                PrintWarning("Use the \"tebex:secret <secret>\" command to update it without having to reload the plugin.");
                return;
            }

            webrequest.Enqueue($"{BASE_URL}/information", "", (code, response) =>
            {
                if (response != null)
                {
                    try
                    {
                        var jObject = JObject.Parse(response);

                        switch (code)
                        {
                            case 200:
                                break;
                            case 403:
                                StartValidationTimer(60f);
                                goto default;
                            default:
                                PrintWarning($"An error occurred whilst fetching your store information: {jObject["error_message"].ToString()}");
                                return;
                        }

                        storeUrl = jObject["account"]["domain"].ToString();

                        Puts("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~");
                        Puts("Successfully retrieved store information from your secret key!");
                        Puts("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~");
                        Puts($"Account: {jObject["account"]["name"].ToString()} ({jObject["account"]["currency"]["iso_4217"].ToString()})");
                        Puts($"Server: {jObject["server"]["name"].ToString()}");
                        Puts($"URL: {storeUrl}");
                        Puts("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~");

                        if (!validated)
                        {
                            validated = true;
                            this.secretKey = secretKey;

                            Config["Secret key of your shop (do not tell it anyone)"] = secretKey;
                            SaveConfig();

                            CheckCommandQueue(true);
                        }
                    }
                    catch (Exception e)
                    {
                        PrintError($"An exception was thrown whilst validating your secret key: {e.Message}");

                        if (debugLogResponseErrors)
                            PrintError($"Associated response: {response}");

                        if (debugLogStackTraces)
                            PrintError(e.StackTrace);
                    }

                    return;
                }

                PrintError($"An unhandled error occurred whilst fetching your store information from the secret key provided (response code: {code}).");
            }, this, RequestMethod.GET, AddToHeaders(secretKey), 3000f);
        }

        private void ProcessCommands(List<Command> commands)
        {
            if (commands.Count < 1)
                return;

            List<Command> executedCommands = new List<Command>();

            foreach (Command command in commands)
            {
                if (debugLogActions)
                {
                    string type = command.Online ? "online" : "offline";
                    PrintWarning($"Processed {type} command for {command.PlayerId}: {command.CommandString}");
                }
                
                executedCommands.Add(command);
                server.Command(command.CommandString);

                if (executedCommands.Count == 15)
                {
                    DeleteCommands(executedCommands);
                    executedCommands.Clear();
                }
            }

            if (executedCommands.Count > 0)
                DeleteCommands(executedCommands);
        }

        private void ProcessOfflineCommands()
        {
            webrequest.Enqueue($"{BASE_URL}/queue/offline-commands", "", (code, response) =>
            {
                if (response != null)
                {
                    try
                    {
                        var jObject = JObject.Parse(response);

                        switch (code)
                        {
                            case 200:
                                break;
                            case 403:
                                StartValidationTimer(60f);
                                goto default;
                            default:
                                PrintWarning($"An error occurred whilst processing the offline commands: {jObject["error_message"].ToString()}");
                                return;
                        }

                        List<Command> commands = new List<Command>();

                        foreach (var command in (JArray) jObject["commands"])
                        {
                            int id = (int) command["id"];
                            string playerId = command["player"]["uuid"].ToString();
                            string commandString = command["command"].ToString().Replace("{id}", playerId).Replace("{username}", command["player"]["name"].ToString());

                            commands.Add(new Command(id, false, playerId, commandString));
                        }

                        ProcessCommands(commands);
                    }
                    catch (Exception e)
                    {
                        PrintError($"An exception was thrown whilst processing the offline commands: {e.Message}");

                        if (debugLogResponseErrors)
                            PrintError($"Associated response: {response}");

                        if (debugLogStackTraces)
                            PrintError(e.StackTrace);
                    }

                    return;
                }

                PrintError($"An unhandled error occurred whilst processing the offline commands (response code: {code}).");
            }, this, RequestMethod.GET, AddToHeaders(secretKey), 3000f);
        }

        private void ProcessOnlineCommands(IPlayer player, string shopPlayerId)
        {
            webrequest.Enqueue($"{BASE_URL}/queue/online-commands/{shopPlayerId}", "", (code, response) =>
            {
                if (response != null)
                {
                    try
                    {
                        var jObject = JObject.Parse(response);

                        switch (code)
                        {
                            case 200:
                                break;
                            case 403:
                                StartValidationTimer(60f);
                                goto default;
                            default:
                                PrintWarning($"An error occurred whilst processing the online commands for {player.Id}: {jObject["error_message"].ToString()}");
                                return;
                        }

                        List<Command> commands = new List<Command>();

                        foreach (var command in (JArray) jObject["commands"])
                        {
                            int id = (int) command["id"];
                            string commandString = command["command"].ToString().Replace("{id}", player.Id).Replace("{username}", player.Name);

                            commands.Add(new Command(id, true, player.Id, commandString));
                        }

                        ProcessCommands(commands);
                    }
                    catch (Exception e)
                    {
                        PrintError($"An exception was thrown whilst processing the online commands for {player.Id}: {e.Message}");

                        if (debugLogResponseErrors)
                            PrintError($"Associated response: {response}");

                        if (debugLogStackTraces)
                            PrintError(e.StackTrace);
                    }

                    return;
                }

                PrintError($"An unhandled error occurred whilst processing the online commands for {player.Id} (response code: {code}).");
            }, this, RequestMethod.GET, AddToHeaders(secretKey), 3000f);
        }

        #endregion

        #region Web Request Timers

        private void StartValidationTimer(float delay = 0f)
        {
            if (validationTimer != null && !validationTimer.Destroyed)
                return;

            if (checkTimer != null && !checkTimer.Destroyed)
                checkTimer.Destroy();

            validated = false;

            if (delay > 0f)
                validationTimer = timer.In(delay, () => FetchStoreInformation(false, secretKey));
            else
                FetchStoreInformation(false, secretKey);
        }

        #endregion

        #region Utilities

        private Dictionary<string, string> AddToHeaders(string secretKey) => AddToHeaders(null, secretKey);

        private Dictionary<string, string> AddToHeaders(Dictionary<string, string> currentHeaders, string secretKey)
        {
            if (currentHeaders == null)
                currentHeaders = new Dictionary<string, string>();

            currentHeaders["X-Buycraft-Secret"] = secretKey;
            return currentHeaders;
        }

        private T GetConfig<T>(string name, T defaultValue) => Config[name] == null ? defaultValue : (T) Convert.ChangeType(Config[name], typeof(T));

        private T GetConfig<T>(string name, string name1, T defaultValue) => Config[name, name1] == null ? defaultValue : (T) Convert.ChangeType(Config[name, name1], typeof(T));

        #endregion

    }

}
