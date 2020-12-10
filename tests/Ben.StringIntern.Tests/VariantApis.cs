using System;
using System.Linq;
using System.Text;

using Ben.Collections.Specialized;

using Xunit;

namespace Ben.StringIntern.Tests
{
    public class VariantApis
    {
        [Fact]
        public void VariantApisInternSameStrings()
        {
            var pool = new InternPool();

            for (int i = 0; i < 126; i++)
            {
                var array = Enumerable.Range(1, i).Select(x => (char)x).ToArray();
                string str = new (array);
                ReadOnlySpan<char> charSpan = str;
                ReadOnlySpan<byte> asciiSpan = Encoding.ASCII.GetBytes(str);
                ReadOnlySpan<byte> utf8Span = Encoding.UTF8.GetBytes(str);

                Assert.Equal(array, str.AsSpan().ToArray());
                Assert.Equal(array, charSpan.ToArray());
                Assert.Equal(array, asciiSpan.ToArray().Select(x => (char)x).ToArray());
                Assert.Equal(array, utf8Span.ToArray().Select(x => (char)x).ToArray());

                string str0 = pool.Intern(str);
                string str1 = pool.Intern(charSpan);
                string str2 = pool.InternAscii(asciiSpan);
                string str3 = pool.InternUtf8(utf8Span);

                Assert.Equal(i, pool.Count);
                Assert.Equal(i * 4, (int)pool.Considered);

                Assert.Same(str0, str1);
                Assert.Same(str0, str2);
                Assert.Same(str0, str3);
            }
        }
    }
}
