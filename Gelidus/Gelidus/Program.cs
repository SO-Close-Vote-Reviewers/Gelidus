/*
 * Gelidus. .Net based anti-freeze bot for SE chat rooms.
 * Copyright © 2015, ArcticEcho.
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
        private static readonly int[] owners = new int[] { 1043380, 578411, 2246344 }; // gunr2171, rene, Sam
        private static readonly Regex spamMessages = new Regex(@"(?i)\bapp\b|\.com", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly ChatterBotFactory factory = new ChatterBotFactory();
        private static readonly ChatterBot bot = factory.Create(ChatterBotType.CLEVERBOT);
        private static readonly ChatterBotSession botSession = bot.CreateSession();
        private static readonly ManualResetEvent stopMre = new ManualResetEvent(false);
        private static System.Timers.Timer convoTimer;
        private static readonly Random random = new Random();
        private static Client chatClient1;
        private static Client chatClient2;
        private static Room bot1Room;
        private static Room bot2Room;



        static void Main(string[] args)
        {
            Console.Write("Setting up bot...");

            TryLogin();
            InitialiseBots();

            var interval = int.Parse(SettingsReader("Interval")) * 1000;
            convoTimer = new System.Timers.Timer(interval) { Enabled = true };
            convoTimer.Elapsed += (o, oo) => ConvoLoop();

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

            Console.WriteLine("done.\nGelidus started, press Q to stop.");

            stopMre.WaitOne();
            convoTimer.Dispose();
            stopMre.Dispose();
            foreach (var r in chatClient1.Rooms)
            {
                r.PostMessage("I've got to go now, bye!");
                r.Leave();
            }
            foreach (var r in chatClient2.Rooms)
            {
                r.Leave();
            }
            chatClient1.Dispose();
            chatClient2.Dispose();
        }



        private static void HandleMention(Room room, Message m)
        {
            try
            {
                if (m.AuthorID == bot1Room.Me.ID || m.AuthorID == bot2Room.Me.ID) { return; }
                if (HandleChatCommand(room, m)) { return; }

                var thought = GetNonSpamMessage(m.Content);
                room.PostReply(m, thought);
            }
            catch (Exception) { }
        }

        private static bool HandleChatCommand(Room room, Message command)
        {
            var cmd = command.Content.Trim().ToUpperInvariant();

            if (cmd.StartsWith("INTERVAL"))
            {
                if (!owners.Contains(command.AuthorID)) { return false; }
                var interval = 0;
                var success = int.TryParse(new string(cmd.Where(Char.IsDigit).ToArray()), out interval);
                if (success)
                {
                    convoTimer.Interval = interval * 1000;
                    return true;
                }
            }

            switch (cmd)
            {
                case "PAUSE":
                {
                    convoTimer.Stop();
                    return true;
                }
                case "RESUME":
                {
                    convoTimer.Start();
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

        private static void ConvoLoop()
        {
            var lastBotMsg = bot1Room.AllMessages.Last(m => m.AuthorID == bot1Room.Me.ID || m.AuthorID == bot2Room.Me.ID);
            try
            {
                if (lastBotMsg.AuthorID == bot2Room.Me.ID)
                {
                    var bot2LastMsg = bot2Room.MyMessages.Last();
                    var thought = GetNonSpamMessage(bot2LastMsg.Content);
                    bot1Room.PostReply(bot2LastMsg, thought);
                }
                else
                {
                    var bot1LastMsg = bot1Room.MyMessages.Last();
                    var thought = GetNonSpamMessage(bot1LastMsg.Content);
                    bot2Room.PostReply(bot1LastMsg, thought);
                }
            }
            catch (Exception) { }
        }

        private static string GetNonSpamMessage(string source)
        {
            string message = null;
            try
            {
                message = botSession.Think(source);

                while (spamMessages.IsMatch(message))
                {
                    message = botSession.Think(source);
                }
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("403"))
                {
                    bot1Room.PostMessage("API quota reached.");
                }
            }

            return message;
        }

        private static void TryLogin()
        {
            var email1 = SettingsReader("bot1 email");
            var pwd1 = SettingsReader("bot1 password");
            chatClient1 = new Client(email1, pwd1);

            var email2 = SettingsReader("bot2 email");
            var pwd2 = SettingsReader("bot2 password");
            chatClient2 = new Client(email2, pwd2);
        }

        private static void InitialiseBots()
        {
            var roomToJoin = SettingsReader("room url");

            bot1Room = chatClient1.JoinRoom(roomToJoin);
            bot1Room.EventManager.ConnectListener(EventType.UserMentioned, new Action<Message>(m => HandleMention(bot1Room, m)));
            Thread.Sleep(random.Next(1, 5000));
            bot1Room.PostMessage("Hey everyone.");

            bot2Room = chatClient2.JoinRoom(roomToJoin);
            bot2Room.EventManager.ConnectListener(EventType.UserMentioned, new Action<Message>(m => HandleMention(bot2Room, m)));
            Thread.Sleep(random.Next(1, 5000));
            bot2Room.PostMessage("Hiya.");
        }

        private static string SettingsReader(string field)
        {
            if (!File.Exists("settings.txt"))
            {
                throw new FileNotFoundException();
            }

            var data = File.ReadAllText("settings.txt");
            var setting = Regex.Match(data, field + @":.*?(\n|\Z)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase).Value;
            setting = setting.Remove(0, setting.IndexOf(":") + 1).Trim();
            return setting;
        }

        private static void StopBot()
        {
            convoTimer.Stop();
            stopMre.Set();
        }
    }
}
