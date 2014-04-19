﻿namespace System.Management.CodeGeneration
{
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
    }
}
