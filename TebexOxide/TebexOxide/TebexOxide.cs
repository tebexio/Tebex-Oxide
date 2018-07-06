using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using Newtonsoft.Json.Linq;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using TebexOxide.Commands;
using TebexOxide.Models;


namespace Oxide.Plugins
{
    [Info("Tebex Oxide", "Tebex", "0.0.1", ResourceId = 0001)]
    [Description("Oxide Plugin for the Tebex Server Monitization Platform.")]
    public class TebexOxide : CovalencePlugin
    {

        public int nextCheck = 15 * 60;
        public WebstoreInfo information;
        private DateTime lastCalled = DateTime.Now.AddMinutes(-14);

        public static TebexOxide Instance;

        public static Timer timerRef;
        
        private String secret = "";
        private String baseUrl = "https://plugin.buycraft.net/";
        private bool buyEnabled = true;
        protected override void LoadDefaultConfig()
        {
            Puts("Creating a new configuration file!");
            if (Config["secret"] == null) Config["secret"] = secret;
            if (Config["baseUrl"] == null) Config["baseUrl"] = baseUrl;
            if (Config["buyEnabled"] == null) Config["buyEnabled"] = true;
            
            SaveConfig();            
        }     
        
        void OnServerInitialized()
        {
            this.information = new WebstoreInfo();
            Instance = this;
            Puts("Tebex Loaded");
            if ((string) Instance.Config["secret"] == "")
            {
                Puts("You have not yet defined your secret key. Use /tebex:secret <secret> to define your key");
            }
            else
            {
                cmdInfo(null, "tebex:info", null);
            }

            timerRef = Instance.timer.Every(60, () =>
            {
                Instance.checkQueue();
            });

            //System.Net.ServicePointManager.ServerCertificateValidationCallback +=
             //   (sender, certificate, chain, errors) => { return true; };
        }

        void Unloaded()
        {
            timerRef.Destroy();
        }
        
        private void checkQueue()
        {
            if ((DateTime.Now - this.lastCalled).TotalSeconds > Instance.nextCheck)
            {
                this.lastCalled = DateTime.Now;
                //Do Command Check             
                cmdForcecheck(null, "tebex:forcecheck", null);
            }
        }

        public void logWarning(String message)
        {
            Puts(message);
        }

        public void UpdateConfig()
        {
            this.SaveConfig();
        }
        
        public IPlayer getPlayerById(String id)
        {
            return players.FindPlayerById(id);
        }

        public bool isPlayerOnline(IPlayer player)
        {
            Puts("IsPlayerOnline" + player.GetType().ToString());
            if (player.GetType().ToString() == "Oxide.Game.SevenDays.Libraries.Covalence.SevenDaysPlayer")
            {
                Puts("Check for online player");
                return players.Connected.Count(p => p.Id == player.Id) > 0;
            }

            return player.IsConnected;
        }

        public void runCommand(string cmd)
        {
            server.Command(cmd);
        }

        [Command("tebex:info")]
        void cmdInfo(IPlayer player, String cmd, String[] args)
        {
            if (player == null || player.IsAdmin)
            {
                CommandTebexInfo cmdCommandTebexInfo = new CommandTebexInfo();
                cmdCommandTebexInfo.Execute(player, cmd, args);
            }
        }
        
        [Command("tebex:secret")]
        void cmdSecret(IPlayer player, String cmd, String[] args)
        {
            if (player.IsServer)
            {
                CommandTebexSecret cmdCommandTebexSecret = new CommandTebexSecret();
                cmdCommandTebexSecret.Execute(player, cmd, args);
            }
        }
        
        [ConsoleCommand("tebex:secret")]
        void cmdSecretConsole(IPlayer player, String cmd, String[] args)
        {
            if (player.IsServer)
            {
                CommandTebexSecret cmdCommandTebexSecret = new CommandTebexSecret();
                cmdCommandTebexSecret.Execute(player, cmd, args);
            }
        }        

        [Command("tebex:forcecheck")]
        void cmdForcecheck(IPlayer player, String cmd, String[] args)
        {
            if (player == null || player.IsAdmin)
            {
                CommandTebexForcecheck cmdCommandTebexForcecheck = new CommandTebexForcecheck();
                cmdCommandTebexForcecheck.Execute(player, cmd, args);
            }
        }

        [Command("buy")]
        void cmdBuy(IPlayer player, String cmd, String[] args)
        {
            player.Message("To buy packages from our webstore, please visit: " + Instance.information.domain);
        }
        
    }
}

namespace TebexOxide.Commands
{
    
    public interface ITebexCommand
    {        
        void Execute(IPlayer player, String cmd, String[] args);
        
        void HandleResponse(JObject response);

        void HandleError(Exception e);
        
    }  
    
    public class CommandTebexSecret : ITebexCommand
    {

        public void Execute(IPlayer player, String cmd, String[] args)
        {

            String secret = args[0];

            Oxide.Plugins.TebexOxide.Instance.Config["secret"] = secret;
            Oxide.Plugins.TebexOxide.Instance.UpdateConfig();
            // Set a custom timeout (in milliseconds)
            var timeout = 2000f;

            // Set some custom request headers (eg. for HTTP Basic Auth)
            var headers = new Dictionary<string, string> { { "X-Buycraft-Secret", (string) Oxide.Plugins.TebexOxide.Instance.Config["secret"] } };
                        
            WebRequests webrequest = new WebRequests();
            webrequest.EnqueueGet((string) Oxide.Plugins.TebexOxide.Instance.Config["baseUrl"] + "information", (code, response) =>
            {
                if (response == null || code != 200)
                {
                    HandleError(new Exception("Error: code" + code.ToString()));
                    webrequest.Shutdown();
                    return;
                }
                
                HandleResponse(JObject.Parse(response));
                webrequest.Shutdown();
                
            }, Oxide.Plugins.TebexOxide.Instance, headers, timeout);

        }

        public void HandleResponse(JObject response)
        {
            Oxide.Plugins.TebexOxide.Instance.information.id = (int) response["account"]["id"];
            Oxide.Plugins.TebexOxide.Instance.information.domain = (string) response["account"]["domain"];
            Oxide.Plugins.TebexOxide.Instance.information.gameType = (string) response["account"]["game_type"];
            Oxide.Plugins.TebexOxide.Instance.information.name = (string) response["account"]["name"];
            Oxide.Plugins.TebexOxide.Instance.information.currency = (string) response["account"]["currency"]["iso_4217"];
            Oxide.Plugins.TebexOxide.Instance.information.currencySymbol = (string) response["account"]["currency"]["symbol"];
            Oxide.Plugins.TebexOxide.Instance.information.serverId = (int) response["server"]["id"];
            Oxide.Plugins.TebexOxide.Instance.information.serverName = (string) response["server"]["name"];
            
            Oxide.Plugins.TebexOxide.Instance.logWarning("Your secret key has been validated! Webstore Name: " + Oxide.Plugins.TebexOxide.Instance.information.name);
        }

        public void HandleError(Exception e)
        {
            Oxide.Plugins.TebexOxide.Instance.logWarning(e.Message);
            Oxide.Plugins.TebexOxide.Instance.logWarning("We were unable to validate your secret key.");
        }      
    }
    
    public class CommandTebexInfo : ITebexCommand
    {      
        public void Execute(IPlayer player, String cmd, String[] args)
        {           
            // Set a custom timeout (in milliseconds)
            var timeout = 2000f;

            // Set some custom request headers (eg. for HTTP Basic Auth)
            var headers = new Dictionary<string, string> { { "X-Buycraft-Secret", (string) Oxide.Plugins.TebexOxide.Instance.Config["secret"] } };

            WebRequests webrequest = new WebRequests();
            webrequest.EnqueueGet((string) Oxide.Plugins.TebexOxide.Instance.Config["baseUrl"] + "information", (code, response) =>
            {
                if (response == null || code != 200)
                {
                    HandleError(new Exception("Error"));
                    webrequest.Shutdown();
                    return;
                }
                
                HandleResponse(JObject.Parse(response));
                webrequest.Shutdown();
                
            }, Oxide.Plugins.TebexOxide.Instance, headers, timeout);

        }

        public void HandleResponse(JObject response)
        {
            Oxide.Plugins.TebexOxide.Instance.information.id = (int) response["account"]["id"];
            Oxide.Plugins.TebexOxide.Instance.information.domain = (string) response["account"]["domain"];
            Oxide.Plugins.TebexOxide.Instance.information.gameType = (string) response["account"]["game_type"];
            Oxide.Plugins.TebexOxide.Instance.information.name = (string) response["account"]["name"];
            Oxide.Plugins.TebexOxide.Instance.information.currency = (string) response["account"]["currency"]["iso_4217"];
            Oxide.Plugins.TebexOxide.Instance.information.currencySymbol = (string) response["account"]["currency"]["symbol"];
            Oxide.Plugins.TebexOxide.Instance.information.serverId = (int) response["server"]["id"];
            Oxide.Plugins.TebexOxide.Instance.information.serverName = (string) response["server"]["name"];
            
            Oxide.Plugins.TebexOxide.Instance.logWarning("Server Information");
            Oxide.Plugins.TebexOxide.Instance.logWarning("=================");
            Oxide.Plugins.TebexOxide.Instance.logWarning("Server "+Oxide.Plugins.TebexOxide.Instance.information.serverName+" for webstore "+Oxide.Plugins.TebexOxide.Instance.information.name+"");
            Oxide.Plugins.TebexOxide.Instance.logWarning("Server prices are in "+Oxide.Plugins.TebexOxide.Instance.information.currency+"");
            Oxide.Plugins.TebexOxide.Instance.logWarning("Webstore domain: "+Oxide.Plugins.TebexOxide.Instance.information.domain+"");
        }

        public void HandleError(Exception e)
        {
            Oxide.Plugins.TebexOxide.Instance.logWarning("We are unable to fetch your server details. Please check your secret key.");
        }      
    }    
    
    public class CommandTebexForcecheck: ITebexCommand
    {

        public void Execute(IPlayer player, String cmd, String[] args)
        {           
            Oxide.Plugins.TebexOxide.Instance.logWarning("Checking for commands to be executed...");
            // Set a custom timeout (in milliseconds)
            var timeout = 2000f;

            // Set some custom request headers (eg. for HTTP Basic Auth)
            var headers = new Dictionary<string, string> { { "X-Buycraft-Secret", (string) Oxide.Plugins.TebexOxide.Instance.Config["secret"] } };

            WebRequests webrequest = new WebRequests();
            webrequest.EnqueueGet((string) Oxide.Plugins.TebexOxide.Instance.Config["baseUrl"] + "queue", (code, response) =>
            {
                if (response == null || code != 200)
                {
                    HandleError(new Exception("Error"));
                    webrequest.Shutdown();
                    return;
                }
                
                HandleResponse(JObject.Parse(response));
                webrequest.Shutdown();
                
            }, Oxide.Plugins.TebexOxide.Instance, headers, timeout);

        }

        public void HandleResponse(JObject response)
        {
            if ((int) response["meta"]["next_check"] > 0)
            {
                Oxide.Plugins.TebexOxide.Instance.nextCheck = (int) response["meta"]["next_check"];
            }
            
            if ((bool) response["meta"]["execute_offline"])
            {
                try
                {
                    TebexCommandRunner.doOfflineCommands();
                }
                catch (Exception e)
                {
                    Oxide.Plugins.TebexOxide.Instance.logWarning(e.ToString());
                }
            }
            
            JArray players = (JArray) response["players"];

            foreach (var player in players)
            {
                try
                {
                    IPlayer targetPlayer = Oxide.Plugins.TebexOxide.Instance.getPlayerById((string) player["uuid"]);                    
                    Oxide.Plugins.TebexOxide.Instance.logWarning(targetPlayer.ToString());

                    if (targetPlayer != null && Oxide.Plugins.TebexOxide.Instance.isPlayerOnline(targetPlayer))
                    {
                        Oxide.Plugins.TebexOxide.Instance.logWarning("Execute commands for " + targetPlayer.Name + "(ID: "+targetPlayer.Id+")");
                        TebexCommandRunner.doOnlineCommands((int) player["id"], (string) targetPlayer.Name, targetPlayer.Id);
                    }
                }
                catch (Exception e)
                {
                    Oxide.Plugins.TebexOxide.Instance.logWarning(e.Message);
                }
            }
        }

        public void HandleError(Exception e)
        {
            Oxide.Plugins.TebexOxide.Instance.logWarning("We are unable to fetch your server queue. Please check your secret key.");
            Oxide.Plugins.TebexOxide.Instance.logWarning(e.ToString());
        }      
    }    
    
    public class TebexCommandRunner
    {

        public static int deleteAfter = 3;
        
        public static void doOfflineCommands()
        {
            String url = Oxide.Plugins.TebexOxide.Instance.Config["baseUrl"] + "queue/offline-commands";

            // Set a custom timeout (in milliseconds)
            var timeout = 2000f;

            // Set some custom request headers (eg. for HTTP Basic Auth)
            var headers = new Dictionary<string, string> { { "X-Buycraft-Secret", (string) Oxide.Plugins.TebexOxide.Instance.Config["secret"] } };

            WebRequests webrequest = new WebRequests();
            webrequest.EnqueueGet(url, (code, response) =>
            {
                JObject json = JObject.Parse(response);
                JArray commands = (JArray) json["commands"];

                int exCount = 0;
                List<int> executedCommands = new List<int>();
                
                foreach (var command in commands.Children())
                {
                    String commandToRun = buildCommand((string) command["command"], (string) command["player"]["name"],
                        (string) command["player"]["uuid"]);
                    
                    Oxide.Plugins.TebexOxide.Instance.logWarning("Run command " + commandToRun);
                    Oxide.Plugins.TebexOxide.Instance.runCommand(commandToRun);
                    executedCommands.Add((int) command["id"]);

                    exCount++;

                    if (exCount % deleteAfter == 0)
                    {
                        try
                        {
                            deleteCommands(executedCommands);
                            executedCommands.Clear();
                        }
                        catch (Exception ex)
                        {
                            Oxide.Plugins.TebexOxide.Instance.logWarning(ex.ToString());
                        }
                    }
                    
                }
                
                Oxide.Plugins.TebexOxide.Instance.logWarning(exCount.ToString() + " offline commands executed");
                if (exCount % deleteAfter != 0)
                {
                    try
                    {
                        deleteCommands(executedCommands);
                        executedCommands.Clear();
                    }
                    catch (Exception ex)
                    {
                        Oxide.Plugins.TebexOxide.Instance.logWarning(ex.ToString());
                    }
                }

                webrequest.Shutdown();
            }, Oxide.Plugins.TebexOxide.Instance, headers, timeout);
        }

        public static void doOnlineCommands(int playerPluginId, string playerName, string playerId)
        {
            
            Oxide.Plugins.TebexOxide.Instance.logWarning("Running online commands for "+playerName+" (" + playerId + ")");
            

            String url = Oxide.Plugins.TebexOxide.Instance.Config["baseUrl"] + "queue/online-commands/" + playerPluginId.ToString();

            // Set a custom timeout (in milliseconds)
            var timeout = 2000f;

            // Set some custom request headers (eg. for HTTP Basic Auth)
            var headers = new Dictionary<string, string> { { "X-Buycraft-Secret", (string) Oxide.Plugins.TebexOxide.Instance.Config["secret"] } };

            WebRequests webrequest = new WebRequests();
            webrequest.EnqueueGet(url, (code, response) =>
            {
                JObject json = JObject.Parse(response);
                JArray commands = (JArray) json["commands"];

                int exCount = 0;
                List<int> executedCommands = new List<int>();
                
                foreach (var command in commands.Children())
                {

                    String commandToRun = buildCommand((string) command["command"], playerName, playerId);
                    
                    Oxide.Plugins.TebexOxide.Instance.logWarning("Run command " + commandToRun);
                    Oxide.Plugins.TebexOxide.Instance.runCommand(commandToRun);
                    executedCommands.Add((int) command["id"]);

                    exCount++;

                    if (exCount % deleteAfter == 0)
                    {
                        try
                        {
                            deleteCommands(executedCommands);
                            executedCommands.Clear();
                        }
                        catch (Exception ex)
                        {
                            Oxide.Plugins.TebexOxide.Instance.logWarning(ex.ToString());
                        }
                    }
                    
                }
                
                Oxide.Plugins.TebexOxide.Instance.logWarning(exCount.ToString() + " online commands executed for " + playerName);
                if (exCount % deleteAfter != 0)
                {
                    try
                    {
                        deleteCommands(executedCommands);
                        executedCommands.Clear();
                    }
                    catch (Exception ex)
                    {
                        Oxide.Plugins.TebexOxide.Instance.logWarning(ex.ToString());
                    }
                }

                webrequest.Shutdown();
            }, Oxide.Plugins.TebexOxide.Instance, headers, timeout);

        }

        public static void deleteCommands(List<int> commandIds)
        {

            String url = Oxide.Plugins.TebexOxide.Instance.Config["baseUrl"] + "queue?";
            String amp = "";

            foreach (int CommandId in commandIds)
            {
                url = url + amp + "ids[]=" + CommandId;
                amp = "&";
            }

            // Set a custom timeout (in milliseconds)
            var timeout = 2000f;

            // Set some custom request headers (eg. for HTTP Basic Auth)
            var headers = new Dictionary<string, string> { { "X-Buycraft-Secret", (string) Oxide.Plugins.TebexOxide.Instance.Config["secret"] } };
            
            WebRequests webrequest = new WebRequests();
            webrequest.Enqueue(url, "", (code, response) =>
            {
                webrequest.Shutdown();
                
            }, Oxide.Plugins.TebexOxide.Instance, RequestMethod.DELETE, headers, timeout);                        
        }

        public static string buildCommand(string command, string username, string id)
        {
            return command.Replace("{id}", id).Replace("{username}", username);
        }
    }    
}

namespace TebexOxide.Models
{
    public class WebstoreInfo
    {
        public int id;
        public string name;
        public string domain;
        public string currency;
        public string currencySymbol;
        public string gameType;
        public string serverName;
        public int serverId;
    }
}
