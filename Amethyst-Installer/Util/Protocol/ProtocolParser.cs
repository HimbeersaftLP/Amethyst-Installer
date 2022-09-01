﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace amethyst_installer_gui.Protocol {
    public static class ProtocolParser {

        private static IProtocolCommand[] m_commandList;
        private static Type[] m_types;

        static ProtocolParser() {
            // Init command list
            try {
                m_types = Assembly.GetExecutingAssembly().GetTypes();
            } catch ( ReflectionTypeLoadException e ) {
                m_types = e.Types.Where(t => t != null).ToArray();
            }
            m_types = m_types.Where(typeof(IProtocolCommand).IsAssignableFrom).ToArray();

            // Init command list from above list
            m_commandList = new IProtocolCommand[m_types.Length - 1]; // subtract 1 because the interface itself is to be excluded
            int indexer = 0;
            for ( int i = 0; i < m_types.Length; i++ ) {
                // Can't implement the interface itself, skip it!
                if ( m_types[i] == typeof(IProtocolCommand) )
                    continue;
                m_commandList[indexer] = ( IProtocolCommand ) Activator.CreateInstance(m_types[i]);
                indexer++;
            }
        }

        /// <summary>
        /// Parses a given series of commands
        /// </summary>
        /// <param name="args">Array of paremters, typically from Main's string[] args</param>
        /// <returns>Whether regular execution of the program shall be interrupted.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ParseCommands(string[] args) {

            for ( int i = 0; i < args.Length; i++ ) {

                Console.WriteLine(args[i]);

                // Check if this item matches a command or not
                if ( IsCommand(ref args[i], out string cmd) ) {

                    // For each command
                    for ( int j = 0; j < m_commandList.Length; j++ ) {

                        if ( ShouldExecute(ref m_commandList[j], ref cmd) ) {
                            return m_commandList[j].Execute(ExtractParameters(ref args, i));
                        }
                    }
                }
            }

            Console.ReadKey();
            return false;
        }

        /// <summary>
        /// Returns whether the input string is a command, and a formatted command (without a command prefix) if valid
        /// </summary>
        /// <param name="input">The input parameter</param>
        /// <param name="formattedCommand">The command itself</param>
        /// <returns>Whether the input is a valid command or not</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsCommand(ref string input, out string formattedCommand) {

            formattedCommand = string.Empty;

            // --XXXXX
            if ( input.Length > 3 && input[0] == '-' && input[1] == '-' ) {

                formattedCommand = input.Substring(2);

                // -XXXXX
            } else if ( input.Length > 2 && input[0] == '-' ) {

                formattedCommand = input.Substring(1);

                // /XXXXX
            } else if ( input.Length > 2 && input[0] == '/' ) {

                formattedCommand = input.Substring(1);
            }
            formattedCommand = formattedCommand.ToLowerInvariant();
            return formattedCommand.Length > 0;
        }

        /// <summary>
        /// Returns whether the command should be executed or not
        /// </summary>
        /// <param name="command">The command to check</param>
        /// <param name="cmd">A formatted command string</param>
        /// <returns>Whether the command should execute or not</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ShouldExecute(ref IProtocolCommand command, ref string cmd) {

            if ( command.Command.Equals(cmd, StringComparison.InvariantCultureIgnoreCase) ) {
                return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string ExtractParameters(ref string[] args, int index) {

            // If less than minimum parameters
            if ( args.Length - index < 2 ) {
                return "";
            }

            // i + 1 is our first entry
            StringBuilder stringBuffer = new StringBuilder();
            for ( int i = index + 1; i < args.Length; i++ ) {
                stringBuffer.Append(args[i] + " ");
            }
            stringBuffer.Remove(stringBuffer.Length - 1, 1);

            return stringBuffer.ToString().Trim();
        }
    }
}
