using System.Globalization;
using DirectorPrompt.Domain.Services;

namespace DirectorPrompt.Agents;

public sealed class ConditionEngine : IConditionEngine
{
    public bool Evaluate(string condition, ConditionContext context)
    {
        var tokens = Tokenizer.Tokenize(condition);
        var parser = new Parser(tokens, context);
        return parser.ParseExpression();
    }

    private enum TokenType
    {
        Identifier,
        Dot,
        StringLiteral,
        NumberLiteral,
        True,
        False,
        Eq,
        Neq,
        Gt,
        Gte,
        Lt,
        Lte,
        And,
        Or,
        Not,
        LParen,
        RParen,
        EndOfInput
    }

    private readonly record struct Token
    (
        TokenType Type,
        string    Text
    );

    private sealed class Tokenizer
    (
        string input
    )
    {
        private int pos;

        public static List<Token> Tokenize(string input)
        {
            var tokenizer = new Tokenizer(input);
            var tokens    = new List<Token>();

            while (true)
            {
                var token = tokenizer.NextToken();
                tokens.Add(token);

                if (token.Type == TokenType.EndOfInput)
                    break;
            }

            return tokens;
        }

        private Token NextToken()
        {
            SkipWhitespace();

            if (pos >= input.Length)
                return new Token(TokenType.EndOfInput, string.Empty);

            var c = input[pos];

            return c switch
            {
                '('                                 => Single(TokenType.LParen),
                ')'                                 => Single(TokenType.RParen),
                '.'                                 => Single(TokenType.Dot),
                '='                                 => ReadEq(),
                '!'                                 => ReadNot(),
                '>'                                 => ReadGt(),
                '<'                                 => ReadLt(),
                '&'                                 => ReadAnd(),
                '|'                                 => ReadOr(),
                '"'                                 => ReadString(),
                _ when char.IsDigit(c)              => ReadNumber(),
                _ when char.IsLetter(c) || c == '_' => ReadIdentifier(),
                _                                   => throw new ArgumentException($"无法识别的字符: '{c}'")
            };
        }

        private Token Single(TokenType type)
        {
            pos++;
            return new Token(type, input[pos - 1].ToString());
        }

        private Token ReadEq()
        {
            pos++;
            Expect('=');
            return new Token(TokenType.Eq, "==");
        }

        private Token ReadNot()
        {
            pos++;

            if (pos < input.Length && input[pos] == '=')
            {
                pos++;
                return new Token(TokenType.Neq, "!=");
            }

            return new Token(TokenType.Not, "!");
        }

        private Token ReadGt()
        {
            pos++;

            if (pos < input.Length && input[pos] == '=')
            {
                pos++;
                return new Token(TokenType.Gte, ">=");
            }

            return new Token(TokenType.Gt, ">");
        }

        private Token ReadLt()
        {
            pos++;

            if (pos < input.Length && input[pos] == '=')
            {
                pos++;
                return new Token(TokenType.Lte, "<=");
            }

            return new Token(TokenType.Lt, "<");
        }

        private Token ReadAnd()
        {
            pos++;
            Expect('&');
            return new Token(TokenType.And, "&&");
        }

        private Token ReadOr()
        {
            pos++;
            Expect('|');
            return new Token(TokenType.Or, "||");
        }

        private Token ReadString()
        {
            pos++;
            var start = pos;

            while (pos < input.Length && input[pos] != '"')
                pos++;

            if (pos >= input.Length)
                throw new ArgumentException("未闭合的字符串字面量");

            var value = input.Substring(start, pos - start);
            pos++;
            return new Token(TokenType.StringLiteral, value);
        }

        private Token ReadNumber()
        {
            var start = pos;

            while (pos < input.Length && (char.IsDigit(input[pos]) || input[pos] == '.'))
                pos++;

            return new Token(TokenType.NumberLiteral, input.Substring(start, pos - start));
        }

        private Token ReadIdentifier()
        {
            var start = pos;

            while (pos < input.Length && (char.IsLetterOrDigit(input[pos]) || input[pos] == '_'))
                pos++;

            var text = input.Substring(start, pos - start);

            return text switch
            {
                "true"  => new Token(TokenType.True,       text),
                "false" => new Token(TokenType.False,      text),
                _       => new Token(TokenType.Identifier, text)
            };
        }

        private void SkipWhitespace()
        {
            while (pos < input.Length && char.IsWhiteSpace(input[pos]))
                pos++;
        }

        private void Expect(char expected)
        {
            if (pos >= input.Length || input[pos] != expected)
                throw new ArgumentException($"期望 '{expected}'");

            pos++;
        }
    }

    private sealed class Parser
    (
        List<Token>      tokens,
        ConditionContext context
    )
    {
        private int pos;

        public bool ParseExpression() =>
            ParseOr();

        private bool ParseOr()
        {
            var left = ParseAnd();

            while (Peek().Type == TokenType.Or)
            {
                pos++;
                var right = ParseAnd();
                left = left || right;
            }

            return left;
        }

        private bool ParseAnd()
        {
            var left = ParseNot();

            while (Peek().Type == TokenType.And)
            {
                pos++;
                var right = ParseNot();
                left = left && right;
            }

            return left;
        }

        private bool ParseNot()
        {
            if (Peek().Type == TokenType.Not)
            {
                pos++;
                return !ParseNot();
            }

            return ParseComparison();
        }

        private bool ParseComparison()
        {
            var left = ParsePrimary();

            var op = Peek();

            if (op.Type is TokenType.Eq or TokenType.Neq or TokenType.Gt
                or TokenType.Gte or TokenType.Lt or TokenType.Lte)
            {
                pos++;
                var right = ParsePrimary();
                return EvaluateComparison(left, op.Type, right);
            }

            return ToBool(left);
        }

        private object? ParsePrimary()
        {
            var token = Peek();

            switch (token.Type)
            {
                case TokenType.LParen:
                    pos++;
                    var result = ParseExpression();

                    if (Peek().Type != TokenType.RParen)
                        throw new ArgumentException("期望 ')'");

                    pos++;
                    return result;

                case TokenType.StringLiteral:
                    pos++;
                    return token.Text;

                case TokenType.NumberLiteral:
                    pos++;
                    return float.Parse(token.Text, CultureInfo.InvariantCulture);

                case TokenType.True:
                    pos++;
                    return true;

                case TokenType.False:
                    pos++;
                    return false;

                case TokenType.Identifier:
                    return ParseReference();

                default:
                    throw new ArgumentException($"意外的 token: {token.Text}");
            }
        }

        private object? ParseReference()
        {
            var prefix = Advance().Text;

            if (Peek().Type != TokenType.Dot)
                throw new ArgumentException($"期望 '.' 在 '{prefix}' 之后");

            pos++;

            if (Peek().Type != TokenType.Identifier)
                throw new ArgumentException("期望标识符");

            var name = Advance().Text;

            return prefix switch
            {
                "state" => context.StateValues.TryGetValue(name, out var value) ?
                               value :
                               null,
                _ => throw new ArgumentException($"未知的引用前缀: '{prefix}'")
            };
        }

        private static bool EvaluateComparison(object? left, TokenType op, object? right) =>
            op switch
            {
                TokenType.Eq  => Equals(left, right),
                TokenType.Neq => !Equals(left, right),
                TokenType.Gt  => CompareNumeric(left, right) > 0,
                TokenType.Gte => CompareNumeric(left, right) >= 0,
                TokenType.Lt  => CompareNumeric(left, right) < 0,
                TokenType.Lte => CompareNumeric(left, right) <= 0,
                _             => false
            };

        private static int CompareNumeric(object? left, object? right)
        {
            var leftNum  = ToFloat(left);
            var rightNum = ToFloat(right);

            return leftNum.CompareTo(rightNum);
        }

        private static float ToFloat(object? value) =>
            value switch
            {
                float f                                                                                           => f,
                int i                                                                                             => i,
                string s when float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) => result,
                bool b => b ?
                              1f :
                              0f,
                null => 0f,
                _    => throw new ArgumentException($"无法将 '{value}' 转换为数值")
            };

        private static bool ToBool(object? value) =>
            value switch
            {
                bool b                                         => b,
                string s when bool.TryParse(s, out var result) => result,
                null                                           => false,
                _                                              => throw new ArgumentException($"无法将 '{value}' 转换为布尔值")
            };

        private Token Peek() =>
            pos < tokens.Count ?
                tokens[pos] :
                new Token(TokenType.EndOfInput, string.Empty);

        private Token Advance()
        {
            var token = Peek();
            pos++;
            return token;
        }
    }
}
