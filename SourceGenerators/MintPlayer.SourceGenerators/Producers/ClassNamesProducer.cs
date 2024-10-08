﻿using MintPlayer.SourceGenerators.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace MintPlayer.SourceGenerators.Producers
{
    public class ClassNamesProducer : Producer
    {
        private readonly IEnumerable<Models.ClassDeclaration> declarations;
        public ClassNamesProducer(IEnumerable<Models.ClassDeclaration> declarations, string rootNamespace) : base(rootNamespace)
        {
            this.declarations = declarations;
        }

        protected override ProducedSource ProduceSource(CancellationToken cancellationToken)
        {
            var source = new StringBuilder();
            source.AppendLine(Header);
            source.AppendLine();
            source.AppendLine($"namespace {RootNamespace};");

            source.AppendLine("public static class ClassNames");
            source.AppendLine("{");

            foreach (var declaration in declarations)
            {
                source.AppendLine($"    public const string {declaration.Name} = \"{declaration.Name}\";");
            }

            source.AppendLine("}");

            var sourceText = source.ToString();
            var fileName = $"ClassNames.g.cs";

            return new ProducedSource { FileName = fileName, Source = sourceText };
        }
    }
}
