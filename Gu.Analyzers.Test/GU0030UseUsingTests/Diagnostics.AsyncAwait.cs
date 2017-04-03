﻿namespace Gu.Analyzers.Test.GU0030UseUsingTests
{
    using System.Threading.Tasks;
    using NUnit.Framework;

    internal partial class Diagnostics : DiagnosticVerifier<GU0030UseUsing>
    {
        internal class AsyncAwait : NestedDiagnosticVerifier<Diagnostics>
        {
            [Test]
            public async Task AwaitTaskRun()
            {
                var testCode = @"
using System;
using System.Threading.Tasks;

internal static class Foo
{
    internal static async Task Bar()
    {
        ↓var disposable = await Task.Run(() => new Disposable());
    }
}";
                var expected = this.CSharpDiagnostic()
                       .WithLocationIndicated(ref testCode)
                       .WithMessage("Use using.");
                await this.VerifyCSharpDiagnosticAsync(new[] { DisposableCode, testCode }, expected).ConfigureAwait(false);
            }

            [Test]
            public async Task AwaitTaskFromResult()
            {
                var testCode = @"
using System;
using System.Threading.Tasks;

internal static class Foo
{
    internal static async Task Bar()
    {
        ↓var disposable = await Task.FromResult(new Disposable());
    }
}";
                var expected = this.CSharpDiagnostic()
                       .WithLocationIndicated(ref testCode)
                       .WithMessage("Use using.");
                await this.VerifyCSharpDiagnosticAsync(new[] { DisposableCode, testCode }, expected).ConfigureAwait(false);
            }

            [Test]
            public async Task AwaitCreateAsync()
            {
                var testCode = @"
using System;
using System.Threading.Tasks;

internal static class Foo
{
    internal static async Task Bar()
    {
        ↓var stream = await CreateAsync();
    }

    internal static async Task<IDisposable> CreateAsync()
    {
        return new Disposable();
    }
}";
                var expected = this.CSharpDiagnostic()
                       .WithLocationIndicated(ref testCode)
                       .WithMessage("Use using.");
                await this.VerifyCSharpDiagnosticAsync(new[] { DisposableCode, testCode }, expected).ConfigureAwait(false);
            }

            [Test]
            public async Task AwaitCreateAsyncTaskFromResult()
            {
                var testCode = @"
using System;
using System.Threading.Tasks;

internal static class Foo
{
    internal static async Task Bar()
    {
        ↓var stream = await CreateAsync();
    }

    internal static Task<Disposable> CreateAsync()
    {
        return Task.FromResult(new Disposable());
    }
}";
                var expected = this.CSharpDiagnostic()
                       .WithLocationIndicated(ref testCode)
                       .WithMessage("Use using.");
                await this.VerifyCSharpDiagnosticAsync(new[] { DisposableCode, testCode }, expected).ConfigureAwait(false);
            }

            [Test]
            public async Task AwaitReadAsync()
            {
                var testCode = @"
using System.IO;
using System.Threading.Tasks;
  
internal static class Foo
{
    internal static async Task Bar()
    {
        ↓var stream = await ReadAsync(string.Empty);
    }

    internal static async Task<Stream> ReadAsync(string file)
    {
        var stream = new MemoryStream();
        using (var fileStream = File.OpenRead(file))
        {
            await fileStream.CopyToAsync(stream)
                            .ConfigureAwait(false);
        }

        stream.Position = 0;
        return stream;
    }
}";
                var expected = this.CSharpDiagnostic()
                       .WithLocationIndicated(ref testCode)
                       .WithMessage("Use using.");
                await this.VerifyCSharpDiagnosticAsync(testCode, expected).ConfigureAwait(false);
            }
        }
    }
}