namespace Gu.Analyzers
{
    internal class StringBuilderType : QualifiedType
    {
        internal readonly QualifiedMethod AppendLine;
        internal readonly QualifiedMethod Append;

        internal StringBuilderType()
            : base("System.Text.StringBuilder")
        {
            this.AppendLine = new QualifiedMethod(this, "AppendLine");
            this.Append = new QualifiedMethod(this, "Append");
        }
    }
}