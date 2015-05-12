/*
 * Gelidus. .Net based anti-freeze bot for SE chat rooms.
 * Copyright © 2015, SO-Close-Vote-Reviewers.
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */





﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ChatExchangeDotNet;
using ChatterBotAPI;

namespace Gelidus
{
    class Program
    {
        private static readonly string[] startUpMessages = new string[] { "Hey", "Hiya", "Hi", "Hello", "And I'm back.", "*returns*", "Hey everyone", "'ello", "Greetings." };
        private static readonly string[] shutdownMessages = new string[] { "Well, cya", "Gtg, bye!", "I'm outta here.", "I'm leaving...", "See you guys later.", "Gtg m8, cya l8r. Bbfn." };
        private static readonly Regex badMessages = new Regex(@"(?i)\bapp\b|\.com|clever\w+|kisses|groans|strokes", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly ChatterBotFactory factory = new ChatterBotFactory();
        private static readonly ChatterBot bot = factory.Create(ChatterBotType.CLEVERBOT);
        private static readonly Dictionary<int, ChatterBotSession> botSessions = new Dictionary<int, ChatterBotSession>();
        private static readonly Dictionary<int, Client> chatClients = new Dictionary<int, Client>();
        private static readonly Dictionary<int, Room> botRooms = new Dictionary<int, Room>();
        private static readonly ManualResetEvent stopMre = new ManualResetEvent(false);
        private static readonly ManualResetEvent pauseMre = new ManualResetEvent(false);
        private static readonly ManualResetEvent convoLoopMre = new ManualResetEvent(false);
        private static readonly List<int> owners = new List<int>();
        private static readonly Random random = new Random();
        private static Message lastBotMessage;
        private static int intervalMilliseconds;
        private static bool shutdown;
        private static bool pause;



        static void Main(string[] args)
        {
            Console.Title = "Gelidus";
            Console.Write("Setting up...");

            InitialiseBots();
            Task.Factory.StartNew(ConvoLoop);

            Task.Factory.StartNew(() =>
            {
                while (true)
                {
                    if (Char.ToLowerInvariant(Console.ReadKey(true).KeyChar) == 'q')
                    {
                        StopBot();
                        return;
                    }
                }
            });

            Console.WriteLine("done.\nGelidus started, press Q to stop.\n");

            stopMre.WaitOne();
        }



        private static void ConvoLoop()
        {
            convoLoopMre.WaitOne(intervalMilliseconds);

            try
            {
                while (true)
                {
                    foreach (var kv in botRooms)
                    {
                        convoLoopMre.WaitOne(intervalMilliseconds);

                        if (shutdown) { return; }
                        if (pause) { pauseMre.WaitOne(); }

                        var thought = GetGoodMessage(kv.Key, lastBotMessage.Content);
                        kv.Value.PostReply(lastBotMessage, thought);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        private static string GetGoodMessage(int botID, string source)
        {
            string message = null;
            try
            {
                message = botSessions[botID].Think(source);

                while (badMessages.IsMatch(message))
                {
                    message = botSessions[botID].Think(source);
                }
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("403"))
                {
                    Console.WriteLine("API quota reached.");
                }
                else
                {
                    Console.WriteLine(ex);
                }
                return null;
            }

            return message;
        }

        private static void InitialiseBots()
        {
            intervalMilliseconds = Math.Max(10, int.Parse(SettingsReader("Interval")) * 1000);
            var botCount = Regex.Matches(File.ReadAllText("settings.txt"), @"(?i)bot(\d+)? email").Count;
            var ids = SettingsReader("owner ids").Split(',').Where(x => !String.IsNullOrEmpty(x));
            var roomToJoin = SettingsReader("room url");
            foreach (var id in ids) { owners.Add(int.Parse(id)); }

            for (var i = 0; i < botCount; i++)
            {
                var botID = i;
                var startUpMessage = startUpMessages[random.Next(0, startUpMessages.Length)];
                var email = SettingsReader("bot" + (botID + 1).ToString() + " email");
                var pwd = SettingsReader("bot" + (botID + 1).ToString() + " password");

                botSessions[botID] = bot.CreateSession();
                chatClients[botID] = new Client(email, pwd);
                botRooms[botID] = chatClients[botID].JoinRoom(roomToJoin);
                botRooms[botID].EventManager.ConnectListener(EventType.UserMentioned, new Action<Message>(m => HandleMention(botID, m)));
                botRooms[botID].PostMessage(startUpMessage);
            }

            botRooms[0].IgnoreOwnEvents = false;
            botRooms[0].EventManager.ConnectListener(EventType.MessagePosted, new Action<Message>(m =>
            {
                if (botRooms.Any(kv => m.AuthorID == kv.Value.Me.ID))
                {
                    lastBotMessage = m;
                }
            }));
            lastBotMessage = botRooms[botCount - 1].MyMessages.Last();
        }

        private static void HandleMention(int botID, Message m)
        {
            try
            {
                if (botRooms.Any(kv => m.AuthorID == kv.Value.Me.ID)) { return; }
                if (HandleChatCommand(botID, m)) { return; }

                var thought = GetGoodMessage(botID, m.Content);
                botRooms[botID].PostReply(m, thought);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        private static bool HandleChatCommand(int botID, Message command)
        {
            var cmd = command.Content.Trim().ToUpperInvariant();

            if (cmd.StartsWith("INTERVAL"))
            {
                if (!owners.Contains(command.AuthorID)) { return false; }
                var interval = 0;
                var success = int.TryParse(new string(cmd.Where(Char.IsDigit).ToArray()), out interval);
                if (success)
                {
                    intervalMilliseconds = Math.Max(10, interval * 1000);
                    convoLoopMre.Set();
                    convoLoopMre.Reset();
                    Console.WriteLine("Interval updated to: " + intervalMilliseconds + " ms");
                    return true;
                }
            }

            switch (cmd)
            {
                case "PAUSE":
                {
                    pauseMre.Reset();
                    pause = true;
                    Console.WriteLine("Bot paused.");
                    return true;
                }
                case "RESUME":
                {
                    pause = false;
                    pauseMre.Set();
                    Console.WriteLine("Bot resumed.");
                    return true;
                }
                case "STOP":
                {
                    if (!owners.Contains(command.AuthorID)) { return false; }
                    StopBot();
                    return true;
                }
                default:
                {
                    return false;
                }
            }
        }

        private static string SettingsReader(string field)
        {
            var data = File.ReadAllText("settings.txt");
            var setting = Regex.Match(data, field + @":.*?(\n|\Z)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase).Value;
            setting = setting.Remove(0, setting.IndexOf(":") + 1).Trim();
            return setting;
        }

        private static void StopBot()
        {
            Console.WriteLine("Stopping...");

            pause = false;
            shutdown = true;

            pauseMre.Set();
            pauseMre.Dispose();
            convoLoopMre.Set();
            convoLoopMre.Dispose();

            foreach (var kv in botRooms)
            {
                var shutdownMessage = shutdownMessages[random.Next(0, shutdownMessages.Length)];
                kv.Value.PostMessage(shutdownMessage);
                kv.Value.Leave();
                chatClients[kv.Key].Dispose();
            }

            stopMre.Set();
            stopMre.Dispose();
        }
    }
}
