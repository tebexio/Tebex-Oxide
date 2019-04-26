using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using UnityEngine;
using WebSocketSharp;

// Code re-worked by Hougan 25.04.2019

namespace Oxide.Plugins
{
    [Info("Tebex Donate", "Tebex", "1.1.1")]
    [Description("Official Plugin for the Tebex Server Monetization Platform.")]
    public class TebexDonate : CovalencePlugin
    {
        #region Classes
        
        private class Configuration
        {
            [JsonProperty("Secret key of your shop (do not tell it anyone)")]
            public string SecretKey;
            [JsonProperty("Enable /buy command")]
            public bool BuyEnabled;
            [JsonProperty("Debug information")]
            public bool DebugInformation;

            public static Configuration Generate()
            {
                Interface.Oxide.LogWarning($"Creating a new configuration file successful!");
                return new Configuration
                {
                    SecretKey  = null,
                    BuyEnabled = true,
                    DebugInformation = true 
                };
            }
        }
        
        #endregion

        #region Variables

        private static string FinalURL = "UNSET";
        private static Coroutine MainProcess = null;
        private static string BaseURL = "https://plugin.buycraft.net/";
        private static Configuration Settings = Configuration.Generate(); 

        #endregion

        #region Initialization

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                Settings = Config.ReadObject<Configuration>();
            }
            catch
            {
                PrintError($"An error occurred reading the configuration file!");
                PrintError($"Check it with any JSON Validator!");
                return;
            }
            
            SaveConfig();  
        } 

        protected override void LoadDefaultConfig() => Settings = Configuration.Generate();
        protected override void SaveConfig()        => Config.WriteObject(Settings);
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["BUY"] = "You can <color=#194a8c>buy packages</color> from our store!\nPlease visit: {url}"
            }, this); 
        }
        
        private void OnServerInitialized()
        {
            if (Settings.SecretKey.IsNullOrEmpty())
            {
                PrintError($"You have not yet defined your secret key! Use 'tebex:secret <secret>' to define your key!");
                return;
            }

            ServerMgr.Instance.StartCoroutine(ValidateSecretKey(Settings.SecretKey));
        }

        private void Unload() => ServerMgr.Instance.StopAllCoroutines();

        #endregion

        #region Commands

        #region Server side

        [Command("tebex:secret")]
        private void CmdSetSecret(IPlayer player, string command, string[] args)
        {
            if (!player.IsServer) return;
            
            if (args.Length == 0)
            {
                PrintError($"You did not enter your secret key!");
                return;
            }

            ServerMgr.Instance.StartCoroutine(ValidateSecretKey(args[0], true));
        }

        [Command("tebex:info")]
        private void CmdInfo(IPlayer player, string command, string[] args)
        {
            if (!player.IsServer) return;
            
            ServerMgr.Instance.StartCoroutine(FetchShopInformation(false));
        }
        
        #endregion

        #region Client side

        [Command("buy")]
        private void CmdBuyInformation(IPlayer player, string command, string[] args)
        {
            if (player.IsServer) return;

            var message = lang.GetMessage("BUY", this, player.Id);
            message = message.Replace("{url}", FinalURL).Replace("{webstoreUrl}", FinalURL);
            
            player.Message(message);  
        }

        #endregion

        #endregion

        #region Methods

        private void ProcessCommands(List<int> commandIds)
        {
            string requestUri = BaseURL + "queue?";
            string amp = "";

            foreach (var check in commandIds)
            {
                PrintWarning($"Delete command: {check}");
                requestUri += $"{amp}ids[]={check}";
                amp = "&";
            }
            
            webrequest.Enqueue(requestUri, "", (code, response) =>
            {
            }, this, RequestMethod.DELETE, new Dictionary<string, string> { ["X-Buycraft-Secret"] = Settings.SecretKey }, 3000);   
        }

        private IEnumerator CheckQueue()
        {
            while (true)
            {
                int timeToNextCheck = 225;
                Debug("Start processing commands in queue");
                webrequest.Enqueue(BaseURL + "queue", "", (code, response) =>
                {
                    if (response == null || code != 200)
                    {
                        PrintError("We are unable to fetch your server queue. Please check your secret key.");
                        return;
                    }

                    try
                    {
                        var jObject = JObject.Parse(response);
                        if ((bool) jObject["meta"]["execute_offline"])
                        {
                            ServerMgr.Instance.StartCoroutine(FetchOfflineCommands());
                        }

                        timeToNextCheck = (int) jObject["meta"]["next_check"] / 4;

                        foreach (var check in (JArray) jObject["players"])
                        {
                            var target = players.FindPlayerById(check["uuid"].ToString());
                            if (target == null || !target.IsConnected) continue;

                            Debug($"Executing commands for {target.Name}");
                            ServerMgr.Instance.StartCoroutine(FetchOnlineCommands(target, check["id"].ToString()));                            
                        }
                    }
                    catch(JsonReaderException)
                    {
                        PrintError($"Wrong response from server, contact owners!");
                    }
                }, this, RequestMethod.GET, new Dictionary<string, string> { ["X-Buycraft-Secret"] = Settings.SecretKey }, 3000);                
                
                yield return new WaitForSeconds(timeToNextCheck);
            }
        }

        private IEnumerator ExecuteOnlineCommands(IPlayer player, JObject jObject)
        {
                     
                var executeCommands = new List<int>();
                    
                foreach (var check in (JArray) jObject["commands"])
                {
                    string command = (string) check["command"];
                    command = command.Replace("{id}", (string) player.Id).Replace("{username}", (string) player.Name); 
                        
                    executeCommands.Add((int) check["id"]);
                    Debug($"Executing command: {command}");
                    server.Command(command);
                    yield return new WaitForSeconds(0.25F);
                    if (executeCommands.Count % 15 == 0)
                    {
                        try
                        {
                            ProcessCommands(executeCommands);
                            executeCommands.Clear();
                        } 
                        catch
                        {
                            Debug("Failed delete executed commands!");
                        }
                    }
                }
                    
                ProcessCommands(executeCommands);           
        }

        private IEnumerator ExecuteOfflineCommands(JObject jObject)
        {
            var executeCommands = new List<int>();
                    
            foreach (var check in (JArray) jObject["commands"])
            {
                string command = (string) check["command"];
                command = command.Replace("{id}", (string) check["player"]["name"]).Replace("{username}", (string) check["player"]["uuid"]); 
                        
                executeCommands.Add((int) check["id"]);
                Debug($"Executing command: {command}");
                server.Command(command);                        
                yield return new WaitForSeconds(0.25F);
                if (executeCommands.Count % 15 == 0)
                {
                    try
                    {
                        ProcessCommands(executeCommands);
                        executeCommands.Clear();
                    }
                    catch 
                    {
                        Debug("Failed delete executed commands!");
                    }
                }
            }
                    
            ProcessCommands(executeCommands);            
        }
        
        private IEnumerator FetchOnlineCommands(IPlayer player, string shopPlayerId)
        {
            Debug("Start processing online commands in queue"); 
            webrequest.Enqueue(BaseURL + $"queue/online-commands/{shopPlayerId}", "", (code, response) =>
            {
                if (response == null || code != 200)
                {
                    PrintError("We are unable to fetch your server queue. Please check your secret key.");
                    return;
                }
                try
                {
                    var jObject = JObject.Parse(response);
                    ServerMgr.Instance.StartCoroutine(ExecuteOnlineCommands(player, jObject));
                }
                catch(JsonReaderException)
                {
                    PrintError($"Wrong response from server, contact owners!");
                }                  
            }, this, RequestMethod.GET, new Dictionary<string, string> { ["X-Buycraft-Secret"] = Settings.SecretKey }, 3000);     
            
            yield return 0;
        }

        private IEnumerator FetchOfflineCommands()
        {
            Debug("Start processing offline commands in queue");
            webrequest.Enqueue(BaseURL + "queue/offline-commands", "", (code, response) =>
            {
                if (response == null || code != 200)
                {
                    PrintError("We are unable to fetch your server queue. Please check your secret key.");
                    return;
                }

                try
                {
                    var jObject = JObject.Parse(response); 
                    ServerMgr.Instance.StartCoroutine(ExecuteOfflineCommands(jObject));
                }
                catch(JsonReaderException)
                {
                    PrintError($"Wrong response from server, contact owners!");
                }
            }, this, RequestMethod.GET, new Dictionary<string, string> { ["X-Buycraft-Secret"] = Settings.SecretKey }, 3000);     
            
            yield return 0;
        }

        private IEnumerator ValidateSecretKey(string key, bool setup = false)
        {
            webrequest.Enqueue(BaseURL + "information", "", (code, response) =>
            {
                if (response == null || code != 200)
                {
                    PrintError($"Something wrong, contact developers [Code: {code}]");
                    return;
                }

                if (setup)
                {
                    PrintWarning($"Congratulations, you confirmed your secret key!"); 
                    
                    Settings.SecretKey = key;
                    SaveConfig();
                }

                ServerMgr.Instance.StartCoroutine(FetchShopInformation(true));
            }, this, RequestMethod.GET, new Dictionary<string, string> { ["X-Buycraft-Secret"] = key });
            
            yield return 0;
        }

        private IEnumerator FetchShopInformation(bool startCheck)
        {
            webrequest.Enqueue(BaseURL + "information", "", (code, response) =>
            {
                if (response == null || code != 200)
                {
                    PrintError($"We are unable to fetch your server details. Please check your secret key.");
                    return;
                }

                try
                {
                    var jObject = JObject.Parse(response);
                    PrintWarning($"~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~");
                    PrintWarning($" {jObject["account"]["domain"]} [{jObject["account"]["currency"]["iso_4217"]}]"); 
                    PrintWarning($"         {jObject["server"]["name"]} for {jObject["account"]["name"]}"); 
                    PrintWarning($"~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~");

                    FinalURL = jObject["account"]["domain"].ToString();
                    if (startCheck)
                    {
                        ServerMgr.Instance.StartCoroutine(CheckQueue());
                    }
                }
                catch(JsonReaderException)
                {
                    PrintError($"Wrong response from server, contact owners!");
                }
                
            }, this, RequestMethod.GET, new Dictionary<string, string> { ["X-Buycraft-Secret"] = Settings.SecretKey }, 3000);
            
            yield return 0;
        }

        #endregion

        #region Utils

        private void Debug(string input)
        {
            if (Settings.DebugInformation) Puts(input);
            LogToFile("Debug", $"{DateTime.Now.ToShortDateString()} {DateTime.Now.ToShortTimeString()} >>> {input}", this); 
        }

        #endregion
    }
}