// Copyright (c) Ben Adams. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Text;

using Ben.Collections.Specialized;

using Xunit;

namespace Ben.StringIntern.Tests
{
    public class HtmlEncoding
    {
        private const int StackAllocThresholdInts = 128;
        internal const int StackAllocThresholdChars = StackAllocThresholdInts * 2;
        internal const int MaxCharExpansionSize = 10;

        [Fact]
        public void ShortHtmlIsEncoded()
        {
            const string value = "<div></div>";

            var pool = new InternPool();

            string str = pool.HtmlEncode(value);

            Assert.NotEqual(value, str);
            Assert.Equal("&lt;div&gt;&lt;/div&gt;", str);

            Assert.Single(pool);
            Assert.Equal(1, (int)pool.Considered);
            Assert.Equal(1, (int)pool.Added);
            Assert.Equal(0, (int)pool.Deduped);

            var strCopy = pool.HtmlEncode(value);

            Assert.Same(strCopy, str);

            Assert.Single(pool);
            Assert.Equal(2, (int)pool.Considered);
            Assert.Equal(1, (int)pool.Added);
            Assert.Equal(1, (int)pool.Deduped);


            strCopy = pool.HtmlEncode(value.AsSpan());

            Assert.Single(pool);
            Assert.Equal(3, (int)pool.Considered);
            Assert.Equal(1, (int)pool.Added);
            Assert.Equal(2, (int)pool.Deduped);
        }

        [Fact]
        public void LargeHtmlIsEncoded()
        {
            char[] array = new char[StackAllocThresholdChars / MaxCharExpansionSize + MaxCharExpansionSize + 1];

            var unicode = "𠜎";

            for (int i = 0; i < array.Length; i += 2)
            {
                array[i] = unicode[0];
                array[i + 1] = unicode[1];
            }

            var value = new string(array);

            var pool = new InternPool();

            string str = pool.HtmlEncode(value);

            Assert.NotEqual(value, str);
            Assert.True(str.Length > value.Length);

            Assert.Single(pool);
            Assert.Equal(1, (int)pool.Considered);
            Assert.Equal(1, (int)pool.Added);
            Assert.Equal(0, (int)pool.Deduped);


            var strCopy = pool.HtmlEncode(value);

            Assert.Same(strCopy, str);

            Assert.Single(pool);
            Assert.Equal(2, (int)pool.Considered);
            Assert.Equal(1, (int)pool.Added);
            Assert.Equal(1, (int)pool.Deduped);

            strCopy = pool.HtmlEncode(value.AsSpan());

            Assert.Single(pool);
            Assert.Equal(3, (int)pool.Considered);
            Assert.Equal(1, (int)pool.Added);
            Assert.Equal(2, (int)pool.Deduped);
        }
    }
}
