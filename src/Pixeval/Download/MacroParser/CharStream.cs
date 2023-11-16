#region Copyright (c) Pixeval/Pixeval
// GPL v3 License
// 
// Pixeval/Pixeval
// Copyright (c) 2022 Pixeval/CharStream.cs
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pixeval.Download.MacroParser;

public class CharStream : ISeekable<char>
{
    private readonly Stack<int> _markers = new();
    private readonly string _text;

    private char[] _stream;

    public CharStream(string text)
    {
        _stream = text.ToCharArray();
        _text = text;
        _markers.Push(0);
    }

    public int Forward { get; private set; }

    public void Seek(int pos)
    {
        _ = _markers.Pop();
        _markers.Push(pos);
        Forward = pos;
    }

    public char Peek()
    {
        return Forward >= _stream.Length ? char.MaxValue : _stream[Forward];
    }

    public void Advance()
    {
        Forward++;
    }

    public void Advance(int n)
    {
        Forward += n;
    }

    public char[] GetWindow()
    {
        return _stream[_markers.Peek()..Forward];
    }

    public void AdvanceMarker()
    {
        _ = _markers.Pop();
        _markers.Push(Forward);
    }

    public void ResetForward()
    {
        Forward = _markers.Peek();
    }

    public void Return()
    {
        Forward--;
    }

    public static async Task<CharStream> Load(Stream stream, Encoding encoding)
    {
        using var sr = new StreamReader(stream, encoding);
        return new CharStream(await sr.ReadToEndAsync());
    }

    public LineInfo GetCurrentLineInfo()
    {
        if (Forward >= _text.Length)
        {
            return LineInfo.Eof;
        }

        var lines = _text[..Forward].Split(Environment.NewLine);
        return new LineInfo(lines.Length, lines[^1].Length);
    }

    public void Replace(char[] newStream)
    {
        _stream = newStream;
        Forward = 0;
        if (_markers.Any())
        {
            _markers.Clear();
        }

        _markers.Push(0);
    }

    public char NextChar()
    {
        var c = Peek();
        Forward++;
        return c;
    }

    public string GetWindowString()
    {
        return new string(_stream[_markers.Peek()..Forward]);
    }

    public void Return(int count)
    {
        Forward -= count;
    }

    public int UntilAndReturn(char c)
    {
        var cnt = 0;
        while (Peek() == c)
        {
            Advance();
            cnt++;
        }

        Return();
        return cnt;
    }

    public int Until(char c)
    {
        var cnt = 0;
        while (Peek() == c)
        {
            Advance();
            cnt++;
        }

        return cnt;
    }

    public bool UntilLimited(char c, int limit)
    {
        var cnt = 0;
        while (Peek() == c)
        {
            if (++cnt > limit)
            {
                return false;
            }

            Advance();
        }

        return true;
    }

    public bool UntilLimitedIf(Func<char, bool> func, int limit)
    {
        var cnt = 0;
        while (func(Peek()))
        {
            if (++cnt > limit)
            {
                return false;
            }

            Advance();
        }

        return true;
    }

    public string GetUntilIf(Func<char, bool> func)
    {
        while (func(Peek()) && Peek() is not char.MaxValue)
        {
            Advance();
        }

        return new string(GetWindow());
    }

    public void PushMarker()
    {
        _markers.Push(Forward);
    }

    public int PopMarker()
    {
        return _markers.Pop();
    }

    public string GetUntilIfAndReturn(Func<char, bool> func)
    {
        var str = GetUntilIf(func);
        Return();
        return str;
    }

    public struct LineInfo(int lineNumber, int position)
    {
        public static readonly LineInfo Eof = new(-1, -1);

        public int LineNumber = lineNumber;

        public int Position = position;
    }
}
