namespace Halite3.hlt
{
    using System;
    using System.Globalization;

    public sealed class InputTokenString
    {
        private readonly string[] tokens;
        private int index;

        public InputTokenString(string text)
        {
            tokens = text.Split(" ", StringSplitOptions.RemoveEmptyEntries);
            index = -1;
        }

        public bool IsEmpty
        {
            get { return (tokens.Length == 0); }
        }

        public string ReadId()
        {
            return ReadToken();
        }

        public int ReadInteger()
        {
            string token = ReadToken();
            return int.Parse(token, CultureInfo.InvariantCulture);
        }

        private string ReadToken()
        {
            index++;
            return tokens[index];
        }
    }
}
