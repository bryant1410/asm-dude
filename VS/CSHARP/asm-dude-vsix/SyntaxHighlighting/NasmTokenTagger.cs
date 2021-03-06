﻿// The MIT License (MIT)
//
// Copyright (c) 2017 Henk-Jan Lebbink
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:

// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.

// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Collections.Generic;

using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Tagging;

using AsmDude.SyntaxHighlighting;
using AsmDude.Tools;
using AsmTools;

namespace AsmDude
{
    internal sealed class NasmTokenTagger : ITagger<AsmTokenTag>
    {
        private readonly ITextBuffer _buffer;
        private readonly AsmDudeTools _asmDudeTools = null;

        private readonly AsmTokenTag _mnemonic;
        private readonly AsmTokenTag _register;
        private readonly AsmTokenTag _remark;
        private readonly AsmTokenTag _directive;
        private readonly AsmTokenTag _constant;
        private readonly AsmTokenTag _jump;
        private readonly AsmTokenTag _label;
        private readonly AsmTokenTag _labelDef;
        private readonly AsmTokenTag _misc;
        private readonly AsmTokenTag _UNKNOWN;

        internal NasmTokenTagger(ITextBuffer buffer)
        {
            this._buffer = buffer;
            this._asmDudeTools = AsmDudeTools.Instance;

            this._mnemonic = new AsmTokenTag(AsmTokenType.Mnemonic);
            this._register = new AsmTokenTag(AsmTokenType.Register);
            this._remark = new AsmTokenTag(AsmTokenType.Remark);
            this._directive = new AsmTokenTag(AsmTokenType.Directive);
            this._constant = new AsmTokenTag(AsmTokenType.Constant);
            this._jump = new AsmTokenTag(AsmTokenType.Jump);
            this._label = new AsmTokenTag(AsmTokenType.Label);
            this._labelDef = new AsmTokenTag(AsmTokenType.LabelDef);
            this._misc = new AsmTokenTag(AsmTokenType.Misc);
            this._UNKNOWN = new AsmTokenTag(AsmTokenType.UNKNOWN);
        }

        event EventHandler<SnapshotSpanEventArgs> ITagger<AsmTokenTag>.TagsChanged {
            add { }
            remove { }
        }

        public IEnumerable<ITagSpan<AsmTokenTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            DateTime time1 = DateTime.Now;

            if (spans.Count == 0)
            {  //there is no content in the buffer
                yield break;
            }

            foreach (SnapshotSpan curSpan in spans)
            {
                ITextSnapshotLine containingLine = curSpan.Start.GetContainingLine();

                string line = containingLine.GetText().ToUpper();
                IList<(int, int, bool)> pos = AsmSourceTools.SplitIntoKeywordPos(line);

                int offset = containingLine.Start.Position;
                int nKeywords = pos.Count;

                for (int k = 0; k < nKeywords; k++)
                {
                    string asmToken = NasmTokenTagger.Keyword(pos[k], line);
                    // keyword starts with a remark char
                    if (AsmSourceTools.IsRemarkChar(asmToken[0]))
                    {
                        yield return new TagSpan<AsmTokenTag>(NasmTokenTagger.New_Span(pos[k], offset, curSpan), this._remark);
                        continue;
                    }

                    // keyword k is a label definition
                    if (pos[k].Item3)
                    {
                        //AsmDudeToolsStatic.Output_INFO("NasmTokenTagger:GetTags: found label " +asmToken);
                        if (IsProperLabelDef(asmToken, containingLine.LineNumber, out AsmTokenTag asmTokenTag))
                        {
                            yield return new TagSpan<AsmTokenTag>(NasmTokenTagger.New_Span(pos[k], offset, curSpan), asmTokenTag);
                            continue;
                        }
                    }

                    AsmTokenType keywordType = this._asmDudeTools.Get_Token_Type(asmToken);
                    switch (keywordType)
                    {
                        case AsmTokenType.Jump:
                        {
                            yield return new TagSpan<AsmTokenTag>(NasmTokenTagger.New_Span(pos[k], offset, curSpan), this._jump);

                            k++; // goto the next word
                            if (k == nKeywords) break; // there are no next words

                            string asmToken2 = NasmTokenTagger.Keyword(pos[k], line);
                            switch (asmToken2)
                            {
                                case "WORD":
                                case "DWORD":
                                case "QWORD":
                                case "SHORT":
                                case "NEAR":
                                {
                                    yield return new TagSpan<AsmTokenTag>(NasmTokenTagger.New_Span(pos[k], offset, curSpan), this._misc);

                                    k++;
                                    if (k == nKeywords) break;
                                    string asmToken3 = NasmTokenTagger.Keyword(pos[k], line);
                                    if (asmToken3.Equals("PTR"))
                                    {
                                        yield return new TagSpan<AsmTokenTag>(New_Span(pos[k], offset, curSpan), this._misc);
                                    } else
                                    {
                                        if (IsProperLabel(asmToken3, containingLine.LineNumber, out AsmTokenTag asmTokenTag))
                                        {
                                            yield return new TagSpan<AsmTokenTag>(NasmTokenTagger.New_Span(pos[k], offset, curSpan), asmTokenTag);
                                        }
                                    }
                                    break;
                                }
                                default:
                                {
                                    if (RegisterTools.IsRegister(asmToken2))
                                    {
                                        yield return new TagSpan<AsmTokenTag>(NasmTokenTagger.New_Span(pos[k], offset, curSpan), this._register);
                                    } else
                                    {
                                        if (IsProperLabel(asmToken2, containingLine.LineNumber, out AsmTokenTag asmTokenTag))
                                        {
                                            yield return new TagSpan<AsmTokenTag>(NasmTokenTagger.New_Span(pos[k], offset, curSpan), asmTokenTag);
                                        }
                                    }
                                    break;
                                }
                            }
                            break;
                        }
                        case AsmTokenType.UNKNOWN: // asmToken is not a known keyword, check if it is numerical
                        {
                            if (AsmTools.AsmSourceTools.IsConstant(asmToken))
                            {
                                yield return new TagSpan<AsmTokenTag>(NasmTokenTagger.New_Span(pos[k], offset, curSpan), this._constant);

                            } else if (asmToken.StartsWith("\"") && asmToken.EndsWith("\""))
                            {
                                yield return new TagSpan<AsmTokenTag>(NasmTokenTagger.New_Span(pos[k], offset, curSpan), this._constant);

                            } else
                            {
                                bool isUnknown = true;

                                // do one word lookahead; see whether we can understand the current unknown word
                                if ((k + 1) < nKeywords)
                                {
                                    k++;
                                    string nextKeyword = NasmTokenTagger.Keyword(pos[k], line);
                                    switch (nextKeyword)
                                    {
                                        case "LABEL":
                                        {
                                            yield return new TagSpan<AsmTokenTag>(NasmTokenTagger.New_Span(pos[k - 1], offset, curSpan), this._labelDef);
                                            yield return new TagSpan<AsmTokenTag>(NasmTokenTagger.New_Span(pos[k], offset, curSpan), this._directive);
                                            isUnknown = false;
                                            break;
                                        }
                                        default:
                                        {
                                            k--;
                                            break;
                                        }
                                    }
                                }

                                // do one word look back; see whether we can understand the current unknown word
                                if (k > 0)
                                {
                                    string previousKeyword = NasmTokenTagger.Keyword(pos[k - 1], line);
                                    switch (previousKeyword)
                                    {
                                        case "ALIAS":
                                        {
                                            yield return new TagSpan<AsmTokenTag>(NasmTokenTagger.New_Span(pos[k], offset, curSpan), this._labelDef);
                                            isUnknown = false;
                                            break;
                                        }
                                        default:
                                        {
                                            break;
                                        }
                                    }
                                }

                                if (isUnknown)
                                {
                                    yield return new TagSpan<AsmTokenTag>(NasmTokenTagger.New_Span(pos[k], offset, curSpan), this._UNKNOWN);
                                }
                            }
                            break;
                        }
                        case AsmTokenType.Directive:
                        {
                            AssemblerEnum assember = this._asmDudeTools.Get_Assembler(asmToken);
                            if (assember.HasFlag(AssemblerEnum.NASM)) // this NASM token-tagger only tags NASM directives
                            {
                                yield return new TagSpan<AsmTokenTag>(NasmTokenTagger.New_Span(pos[k], offset, curSpan), this._directive);
                            }
                            break;
                        }
                        default:
                        {
                            yield return new TagSpan<AsmTokenTag>(NasmTokenTagger.New_Span(pos[k], offset, curSpan), new AsmTokenTag(keywordType));
                            break;
                        }
                    }
                }
            }
            AsmDudeToolsStatic.Print_Speed_Warning(time1, "NasmTokenTagger");
        }

        #region Public Static Methods

        public static bool Advance(
            ref int tokenId,
            ref int curLoc,
            ref int nextLoc,
            out string asmToken,
            out SnapshotSpan? asmTokenSpan,
            string[] tokens,
            SnapshotSpan curSpan)
        {
            var tup = Get_Next_Token(tokenId, nextLoc, tokens);
            tokenId = tup.Item2;
            nextLoc = tup.Item3;

            if (tup.Item1)
            {
                asmToken = tup.Item4;
                curLoc = nextLoc - (asmToken.Length + 1);

                asmTokenSpan = new SnapshotSpan(curSpan.Snapshot, new Span(curLoc, asmToken.Length));
                //if (asmTokenSpan.Value.IntersectsWith(curSpan)) {
                return true;
                //TODO find out what it means if not asmTokenSpan.Value.IntersectsWith(curSpan)
                //}
            } else
            {
                asmToken = null;
                asmTokenSpan = null;
                return false;
            }
        }

        // return true, nextTokenId, tokenEndPos, tokenString
        public static (bool, int, int, string) Get_Next_Token(int tokenId, int startLoc, string[] tokens)
        {
            int nextTokenId = tokenId;
            int nextLoc = startLoc;

            while (nextTokenId < tokens.Length)
            {
                string asmToken = tokens[nextTokenId];
                nextTokenId++;
                if (asmToken.Length > 0)
                {
                    nextLoc += asmToken.Length + 1; //add an extra char location because of the separator
                    return (true, nextTokenId, nextLoc, asmToken.ToUpper());
                } else
                {
                    nextLoc++;
                }
            }
            return (false, nextTokenId, nextLoc, "");
        }

        public static string Keyword((int, int, bool) pos, string line)
        {
            return line.Substring(pos.Item1, pos.Item2 - pos.Item1);
        }

        public static SnapshotSpan New_Span((int, int, bool) pos, int offset, SnapshotSpan lineSnapShot)
        {
            return new SnapshotSpan(lineSnapShot.Snapshot, new Span(pos.Item1 + offset, pos.Item2 - pos.Item1));
        }
        #endregion Public Static Methods

        #region Private Member Methods

        private bool IsProperLabelDef(string asmToken, int lineNumber, out AsmTokenTag labelDefSpan)
        {
            labelDefSpan = null;
            if (!IsExecutableCode(lineNumber)) return false;

            if (asmToken.StartsWith("."))
            {
                if (Get_Last_Non_Local_Label(lineNumber, out string lastNonLocalLabel))
                {
                    labelDefSpan = new AsmTokenTag(AsmTokenType.LabelDef, lastNonLocalLabel);
                    return true;
                }
            } else
            {
                labelDefSpan = this._labelDef;
                return true;
            }
            return false;
        }

        private bool IsProperLabel(string asmToken, int lineNumber, out AsmTokenTag labelSpan)
        {
            labelSpan = null;

            //AsmDudeToolsStatic.Output_INFO("NasmTokenTagger:GetTags: found label " +asmToken);
            if (asmToken.StartsWith("."))
            {
                if (Get_Last_Non_Local_Label(lineNumber, out string lastNonLocalLabel))
                {
                    labelSpan = new AsmTokenTag(AsmTokenType.Label, lastNonLocalLabel);
                    return true;
                }
            } else
            {
                labelSpan = this._label;
                return true;
            }
            labelSpan = null;
            return false;
        }

        private bool IsExecutableCode(int lineNumber)
        {
            for (int i = lineNumber - 1; i >= 0; --i)
            {
                string line = this._buffer.CurrentSnapshot.GetLineFromLineNumber(i).GetText();
                IList<(int, int, bool)> pos = AsmSourceTools.SplitIntoKeywordPos(line);
                if ((pos.Count > 0) && !pos[0].Item3)
                {
                    string keywordString = NasmTokenTagger.Keyword(pos[0], line).ToUpper();
                    if (AsmSourceTools.ParseMnemonic(keywordString) != Mnemonic.UNKNOWN)
                    {
                        return true;
                    }
                    switch (keywordString)
                    {
                        case "STRUC": return false;
                        case "ENDSTRUC": return true;
                    }
                }
                if ((pos.Count > 1) && !pos[1].Item3)
                {
                    string keywordString = NasmTokenTagger.Keyword(pos[1], line).ToUpper();
                    if (AsmSourceTools.ParseMnemonic(keywordString) != Mnemonic.UNKNOWN)
                    {
                        return true;
                    }
                }
            }
            return true;
        }

        private bool Get_Last_Non_Local_Label(int lineNumber, out string lastNonLocalLabel)
        {
            //AsmDudeToolsStatic.Output_INFO("NasmTokenTagger:get_Last_Non_Local_Label: lineNumber=" + lineNumber);
            lastNonLocalLabel = null;

            for (int i = lineNumber-1; i >= 0; --i)
            {
                string line = this._buffer.CurrentSnapshot.GetLineFromLineNumber(i).GetText();
                (int, int, bool) pos = AsmSourceTools.Get_First_Keyword(line);
                string keywordString = NasmTokenTagger.Keyword(pos, line);

                if (pos.Item3)
                {
                    if (!keywordString[0].Equals('.'))
                    {
                        //AsmDudeToolsStatic.Output_INFO("NasmTokenTagger:Get_Last_Non_Local_Label: found label \"" + keywordString + "\" at lineNumber " + i + "; beginPos=" + pos.Item1 + "; endPos=" + pos.Item2);
                        lastNonLocalLabel = keywordString;
                        return true;
                    }
                }
            }
            //AsmDudeToolsStatic.Output_INFO("NasmTokenTagger:get_Last_Non_Local_Label: could not find regular label before lineNumber " + lineNumber);
            return false;
        }
        #endregion
    }
}
