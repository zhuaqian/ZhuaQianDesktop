using System;
using System.Collections.Generic;

namespace ZhuaQianDesktopApp.Tools
{
    // A built-in command the user can invoke from the command palette (/help, /mode, etc.)
    public class SmartCommand
    {
        public string Verb;
        public string Description;
        public Func<ParsedCommand, string> Handler;
    }

    // Registry of built-in slash commands. Each handler receives the parsed command and
    // returns a short status string shown to the user (or null/empty for no message).
    public class SmartCommandRegistry
    {
        readonly List<SmartCommand> commands = new List<SmartCommand>();

        public void Register(string verb, string description, Func<ParsedCommand, string> handler)
        {
            commands.Add(new SmartCommand { Verb = verb.ToLowerInvariant(), Description = description, Handler = handler });
        }

        public List<SmartCommand> All()
        {
            return new List<SmartCommand>(commands);
        }

        public SmartCommand Find(string verb)
        {
            string v = (verb ?? "").ToLowerInvariant();
            foreach (var c in commands)
                if (c.Verb == v) return c;
            return null;
        }

        // Runs the command if known. Returns the handler result, or a help string if unknown.
        public string Execute(ParsedCommand cmd)
        {
            if (cmd == null || !cmd.IsCommand) return null;
            var c = Find(cmd.Verb);
            if (c == null)
                return "Unknown command: /" + cmd.Verb + ". Type /help for the list.";
            if (c.Handler == null) return "";
            return c.Handler(cmd);
        }

        public string HelpText()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Available commands:");
            foreach (var c in commands)
                sb.AppendLine("  /" + c.Verb + " - " + c.Description);
            return sb.ToString();
        }
    }
}
