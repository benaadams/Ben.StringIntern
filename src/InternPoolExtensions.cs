// Copyright (c) Ben Adams. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Text.Encodings.Web;

namespace Ben.Collections.Specialized
{
    public static class InternPoolExtensions
    {
        // 32bit hex entity &#xffff0000;
        internal const int MaxCharExpansionSize = 12;

        public static string HtmlEncode(this InternPool pool, string input)
            => HtmlEncode(pool, input.AsSpan());

#if NET5_0
        public static string HtmlEncode(this InternPool pool, ReadOnlySpan<char> input)
        {
            // Need largest size, can't do multiple rounds of encoding due to https://github.com/dotnet/runtime/issues/45994
            if ((long)input.Length * MaxCharExpansionSize <= InternPool.StackAllocThresholdChars)
            {
                Span<char> output = stackalloc char[InternPool.StackAllocThresholdChars];
                var status = HtmlEncoder.Default.Encode(input, output, out int charsConsumed, out int charsWritten, isFinalBlock: true);
                
                if (status != OperationStatus.Done)
                    throw new InvalidOperationException("Invalid Data");

                output = output.Slice(0, charsWritten);
                return pool.Intern(output);
            }

            return HtmlEncodeSlower(pool, input);
        }


        public static string HtmlEncodeSlower(this InternPool pool, ReadOnlySpan<char> input)
#else
        public static string HtmlEncode(this InternPool pool, ReadOnlySpan<char> input)
#endif
        {
            // Need largest size, can't do multiple rounds of encoding due to https://github.com/dotnet/runtime/issues/45994
            var array = ArrayPool<char>.Shared.Rent(input.Length * MaxCharExpansionSize);
            Span<char> output = array;

            var status = HtmlEncoder.Default.Encode(input, output, out _, out int charsWritten, isFinalBlock: true);

            if (status != OperationStatus.Done)
                throw new InvalidOperationException("Invalid Data");

            output = output.Slice(0, charsWritten);
            string result = pool.Intern(output);

            ArrayPool<char>.Shared.Return(array);

            return result;
        }
    }
}
