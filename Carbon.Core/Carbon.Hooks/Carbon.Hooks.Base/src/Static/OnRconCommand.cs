﻿using System;
using System.Runtime.Serialization;
using API.Commands;
using API.Hooks;
using Carbon.Extensions;
using Facepunch;
using Facepunch.Extend;
using static ConsoleSystem;
using Command = Oxide.Game.Rust.Libraries.Command;

/*
 *
 * Copyright (c) 2022-2023 Carbon Community 
 * All rights reserved.
 *
 */

namespace Carbon.Hooks;
#pragma warning disable IDE0051

public partial class Category_Static
{
	public partial class Static_RCon
	{
		[HookAttribute.Patch("OnRconCommand", "OnRconCommand", typeof(RCon), "OnCommand", new System.Type[] { typeof(Facepunch.RCon.Command) })]
		[HookAttribute.Identifier("ccce0832a0eb4c28bc2372f5e0812c7e")]
		[HookAttribute.Options(HookFlags.Static | HookFlags.IgnoreChecksum)]

		// Called when an RCON command is run.

		public class Static_RCon_ccce0832a0eb4c28bc2372f5e0812c7e : Patch
		{
			internal static string[] EmptyArgs = new string[0];

			public static bool Prefix(RCon.Command cmd)
			{
				if (Community.Runtime == null) return true;

				RCon.responseIdentifier = cmd.Identifier;
				RCon.responseConnection = cmd.ConnectionId;
				RCon.isInput = false;

				try
				{
					var split = cmd.Message.Split(ConsoleArgEx.CommandSpacing, StringSplitOptions.RemoveEmptyEntries);
					var command = split[0].Trim();

					var arguments = split.Length > 1 ? cmd.Message.Substring(command.Length + 1).SplitQuotesStrings() : EmptyArgs;
					var consoleArg = FormatterServices.GetUninitializedObject(typeof(Arg)) as Arg;
					var option = Option.Unrestricted;
					option.FromRcon = true;
					consoleArg.Option = option;
					consoleArg.FullString = cmd.Message;
					consoleArg.Args = arguments;

					if (HookCaller.CallStaticHook("OnRconCommand", cmd.Ip, command, arguments) != null)
					{
						return false;
					}

					var commandArgs = Facepunch.Pool.Get<API.Commands.Command.Args>();
					commandArgs.Arguments = arguments;

					Command.FromRcon = true;

					try
					{
						if (Community.Runtime.CommandManager.Contains(Community.Runtime.CommandManager.RCon, command, out var outCommand))
						{
							Community.Runtime.CommandManager.Execute(outCommand, commandArgs);
						}
						else if (Community.Runtime.CommandManager.Contains(Community.Runtime.CommandManager.Console, command, out var outCommand2))
						{
							Community.Runtime.CommandManager.Execute(outCommand2, commandArgs);
						}
					}
					catch (Exception ex)
					{
						Logger.Error("RconCommand_OnCommand", ex);
					}
				}
				catch { }

				return true;
			}
		}
	}
}