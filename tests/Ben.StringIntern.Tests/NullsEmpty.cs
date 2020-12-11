using System;
using System.Linq;
using System.Text;

using Ben.Collections.Specialized;

using Xunit;

namespace Ben.StringIntern.Tests
{
    public class NullsEmpty
    {
        [Fact]
        public void NullStringsNotAdded()
        {
            var pool = new InternPool();

            string str = pool.Intern(null);
            Assert.Null(str);

            Assert.Empty(pool);
            Assert.Equal(0, pool.Count);
            Assert.Equal(1, (int)pool.Considered);
            Assert.Equal(0, (int)pool.Added);
            Assert.Equal(1, (int)pool.Deduped);
        }

        [Fact]
        public void EmptyStringsNotAdded()
        {
            var pool = new InternPool();

            string str;

            str = pool.Intern(default(ReadOnlySpan<char>));
            Assert.Same(string.Empty, str);

            str = pool.InternAscii(default(ReadOnlySpan<byte>));
            Assert.Same(string.Empty, str);

            str = pool.InternUtf8(default(ReadOnlySpan<byte>));
            Assert.Same(string.Empty, str);

            str = pool.Intern(string.Empty);
            Assert.Same(string.Empty, str);

            Assert.Empty(pool);
            Assert.Equal(0, pool.Count);
            Assert.Equal(4, (int)pool.Considered);
            Assert.Equal(0, (int)pool.Added);
            Assert.Equal(4, (int)pool.Deduped);
        }
    }
}
