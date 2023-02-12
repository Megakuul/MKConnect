using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Renci.SshNet;
using System.Threading;
using System.Runtime.CompilerServices;
using CommandLine;

namespace MSKConnect
{
    internal class Program
    {
        static SshClient client { get; set; }
        static ShellStream shell { get; set; }

        static string hostname { get; set; }
        static string username { get; set; }
        static string password { get; set; }
        static string keyloc { get; set; }
        static string confloc { get; set; }

        static int port { get; set; } = 22;
        static string terminalType { get; set; } = "xterm";

        const string errormsg =
            "An error occurred while reading the arguments.\r\n" +
            "Try again and make sure the arguments are entered as shown below\r\n\r\n";

        const string helpmsg =
            "mkconnect: command-line ssh connection utility\r\n\r\n" +
            "Options:\r\n" +
            "   -h      hostname\r\n" +
            "   -u      username\r\n" +
            "   -p      port [default 22]\r\n" +
            "   -k      location of ssh-key file\r\n" +
            "   -c      location of mkconfig\r\n" +
            "   -t      terminal type (xterm, vt100) [default xterm]\r\n"
        ;

        static void Main(string[] args)
        {
            if (!validateArguments(args))
            {
                Console.WriteLine(helpmsg);
                return;
            }

            if (!fetchInformation())
            {
                Console.WriteLine(errormsg);
                Console.WriteLine(helpmsg);
                return;
            }

            if (!connectSSH(hostname, username, password))
            {
                Console.WriteLine("Failed to connect to ssh target...");
                return;
            }


            //sends CTRL-C to the shell instead of exiting the program
            Console.CancelKeyPress += (object sender, ConsoleCancelEventArgs e) =>
            {
                //Checks if the ConsoleSpecialKey is CTRL-C and not BREAK
                if (e.SpecialKey == ConsoleSpecialKey.ControlC)
                {
                    e.Cancel = true;
                    shell.Write("\x03");
                }
            };

            //Start Shell Loop
            writeToShell();
        }

        /// <summary>
        /// Controls and parses the args
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        static bool validateArguments(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine(errormsg);
                return false;
            }

            if (args[0] == "help")
                return false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "help":
                        return false;
                    case "-h":
                        if (args.Length <= i + 1)
                        {
                            Console.WriteLine(errormsg);
                            return false;
                        }
                        hostname = args[i + 1];
                        i++;
                        break;
                    case "-u":
                        if (args.Length <= i + 1)
                        {
                            Console.WriteLine(errormsg);
                            return false;
                        }
                        username = args[i + 1];
                        i++;
                        break;
                    case "-k":
                        if (args.Length <= i + 1)
                        {
                            Console.WriteLine(errormsg);
                            return false;
                        }
                        keyloc = args[i + 1];
                        i++;
                        break;
                    case "-c":
                        if (args.Length <= i + 1)
                        {
                            Console.WriteLine(errormsg);
                            return false;
                        }
                        keyloc = args[i + 1];
                        i++;
                        break;
                    case "-t":
                        if (args.Length <= i + 1)
                        {
                            Console.WriteLine(errormsg);
                            return false;
                        }
                        terminalType = args[i + 1];
                        i++;
                        break;
                    case "-p":
                        if (args.Length <= i + 1)
                        {
                            Console.WriteLine(errormsg);
                            return false;
                        }
                        port = Convert.ToInt32(args[i + 1]);
                        i++;
                        break;

                }
            }
            if (hostname == null)
            {
                Console.WriteLine(errormsg);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Fetches missing information like username, password etc.
        /// </summary>
        /// <returns></returns>
        static bool fetchInformation()
        {
            if (hostname == null)
                return false;

            if (username == null)
            {
                Console.Write("username: ");
                username = Console.ReadLine();
            }

            if (keyloc != null)
                return true;

            if (password != null)
                return true;

            //Blur password
            ConsoleKeyInfo key;

            Console.Write("password: ");

            string tempPassword = "";
            do
            {
                key = Console.ReadKey(true);

                if (key.Key == ConsoleKey.Backspace && tempPassword.Length > 0)
                {
                    tempPassword = tempPassword.Substring(0, (tempPassword.Length - 1));
                    Console.Write("\b \b");
                    continue;
                }

                if (!Char.IsControl(key.KeyChar))
                {
                    tempPassword += key.KeyChar;
                    Console.Write("*");
                }
            }
            while (key.Key != ConsoleKey.Enter);

            Console.Write("\r\n");

            password = tempPassword;

            return true;
        }

        /// <summary>
        /// Creates a recursive loop to run Commands
        /// </summary>
        static void writeToShell()
        {
            //get the next Key
            var curKey = Console.ReadKey(true);

            //Check if the Key is an Special Key, if it is send special char-combination to the Stream

            if (curKey.Key == ConsoleKey.UpArrow)
                shell.Write("\x1b" + "[A");
            else if (curKey.Key == ConsoleKey.DownArrow)
                shell.Write("\x1b" + "[B");
            else if (curKey.Key == ConsoleKey.RightArrow)
                shell.Write("\x1b" + "[C");
            else if (curKey.Key == ConsoleKey.LeftArrow)
                shell.Write("\x1b" + "[D");
            else if (curKey.Modifiers == ConsoleModifiers.Control && curKey.Key == ConsoleKey.R)
                rewrapEmulator();
            else if (curKey.Modifiers == ConsoleModifiers.Control && curKey.Key == ConsoleKey.F1)
                Environment.Exit(0);
            else
                shell.Write(curKey.KeyChar.ToString());

            writeToShell();
        }

        /// <summary>
        /// Connects to a ssh session
        /// </summary>
        /// <param name="host">hostname</param>
        /// <param name="username">username</param>
        /// <param name="password">password</param>
        /// <returns>if connected succesfully => true</returns>
        static bool connectSSH(string host, string username, string password, string keyloc = null)
        {
            Console.WriteLine($"Connecting to remotehost {host}...");
            //creates connection client
            try
            {
                if (keyloc!= null)
                    client = new SshClient(host, port, username, new PrivateKeyFile(keyloc));
                else
                    client = new SshClient(host, port, username, password);
                client.Connect();
            }
            catch
            {
                return false;
            }

            //create shell
            return rewrapEmulator();
        }

        /// <summary>
        /// Rewraps the Emulator
        /// </summary>
        static bool rewrapEmulator()
        {
            try
            {
                shell = client.CreateShellStream(terminalType, (uint)Console.WindowWidth, (uint)Console.WindowHeight, (uint)Console.BufferWidth, (uint)Console.BufferHeight, 8000);
                shell.DataReceived += Shell_DataReceived;
                shell.Flush();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Eventhandler to get and decode the data from the stream
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void Shell_DataReceived(object sender, Renci.SshNet.Common.ShellDataEventArgs e)
        {
            Console.Write(Encoding.Default.GetString(e.Data));
        }
    }
}