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
        private static readonly int[] owners = new int[] { 1043380, 578411, 2246344 }; // gunr2171, rene, Sam
        private static readonly Regex spamMessages = new Regex(@"(?i)\bapp\b|\.com|clever\w+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly ChatterBotFactory factory = new ChatterBotFactory();
        private static readonly ChatterBot bot = factory.Create(ChatterBotType.CLEVERBOT);
        private static readonly ChatterBotSession botSession = bot.CreateSession();
        private static readonly ManualResetEvent stopMre = new ManualResetEvent(false);
        private static readonly ManualResetEvent pauseMre = new ManualResetEvent(false);
        private static readonly ManualResetEvent convoLoopMre = new ManualResetEvent(false);
        private static readonly Random random = new Random();
        private static Client chatClient1;
        private static Client chatClient2;
        private static Room bot1Room;
        private static Room bot2Room;
        private static int intervalMilliseconds;
        private static bool shutdown;
        private static bool pause;



        static void Main(string[] args)
        {
            Console.Title = "Gelidus";
            Console.Write("Setting up...");

            TryLogin();
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
                    interval = Math.Max(10, interval * 1000);
                    return true;
                }
            }

            switch (cmd)
            {
                case "PAUSE":
                {
                    pause = true;
                    return true;
                }
                case "RESUME":
                {
                    pause = false;
                    pauseMre.Set();
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
            convoLoopMre.WaitOne(intervalMilliseconds);
            var lastBotMessage = bot2Room.MyMessages.Last();
            Message msg;

            while (!shutdown)
            {
                if (pause) { pauseMre.WaitOne(); }

                try
                {
                    var thought = GetNonSpamMessage(lastBotMessage.Content);
                    if (lastBotMessage.AuthorID == bot1Room.Me.ID)
                    {
                        msg = bot2Room.PostReply(lastBotMessage, thought);
                    }
                    else
                    {
                        msg = bot1Room.PostReply(lastBotMessage, thought);
                    }
                    if (msg != null) { lastBotMessage = msg; }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }

                convoLoopMre.WaitOne(intervalMilliseconds);
            }
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
                    Console.WriteLine("API quota reached.");
                    return null;
                }
                else
                {
                    Console.WriteLine(ex);
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
            bot1Room.PostMessage("And I'm back, again.");

            Thread.Sleep(random.Next(1, 15000));

            bot2Room = chatClient2.JoinRoom(roomToJoin);
            bot2Room.EventManager.ConnectListener(EventType.UserMentioned, new Action<Message>(m => HandleMention(bot2Room, m)));
            Thread.Sleep(random.Next(1, 5000));
            bot2Room.PostMessage("Hey.");

            intervalMilliseconds = Math.Max(10, int.Parse(SettingsReader("Interval")) * 1000);
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

            bot1Room.PostMessage("I'm leaving...");
            bot1Room.Leave();
            chatClient1.Dispose();

            Thread.Sleep(random.Next(1, 5000));

            bot2Room.PostMessage("Cya");
            bot2Room.Leave();
            chatClient2.Dispose();

            stopMre.Set();
            stopMre.Dispose();
        }
    }
}
