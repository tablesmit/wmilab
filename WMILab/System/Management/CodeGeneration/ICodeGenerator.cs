﻿/*
 * Copyright (c) 2014 Ryan Armstrong (www.cavaliercoder.com)
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to
 * deal in the Software without restriction, including without limitation the
 * rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
 * sell copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * The Software shall be used for Good, not Evil.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 */
namespace System.Management.CodeGeneration
{
    using System;
    using System.Windows.Forms;

    public interface ICodeGenerator
    {
        /// <summary>Gets the name of this code generator.</summary>
        String Name { get; }

        /// <summary>Gets the programming language of code generated by this code generator.</summary>
        String Language { get; }

        /// <summary>Gets the file extension for files created with this code generator.</summary>
        String FileExtension { get; }

        /// <summary>Gets the name of the Scintilla.Net lexer used for syntax highlighting with this code generator.</summary>
        /// <remarks>http://scintillanet.codeplex.com/wikipage?title=HowToSyntax</remarks>
        String Lexer { get; }

        String GetScript(ManagementClass c, String query);

        CodeGeneratorAction[] GetActions(ManagementClass c, String query);

        Int32 ExecuteAction(CodeGeneratorAction action, ManagementClass c, String query);
    }

    public static class ICodeGeneratorExtensions
    {
        private const String URL = "http://www.cavaliercoder.com/wmilab";

        public static String GetVbStyleHeader(this ICodeGenerator generator)
        {
            return String.Format("' Generated by {0} v.{1}\r\n' {2} \r\n",
                Application.ProductName,
                Application.ProductVersion,
                URL);
        }

        public static String GetCStyleHeader(this ICodeGenerator generator)
        {
            return String.Format("/*\r\n * Generated by {0} v.{1}\r\n * {2}\r\n */\r\n",
                Application.ProductName,
                Application.ProductVersion,
                URL);
        }

        public static String GetPerlStyleHeader(this ICodeGenerator generator)
        {
            return String.Format("=pod\r\n\r\n=head1 AUTHOR\r\n\r\n    Generated by {0} v.{1}\r\n    {2}\r\n\r\n=cut\r\n",
                Application.ProductName,
                Application.ProductVersion,
                URL);
        }
    }
}
