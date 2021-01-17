using System;
using System.Linq;
using System.Text;

using Ben.Collections;

using Xunit;

namespace Ben.StringIntern.Tests
{
    public class StringBuilderTests
    {
        [Fact]
        public void StringBuilderIntern()
        {
            var sb = new StringBuilder();

            for (int length = 0; length < 255; length++)
            {
                for (int i = 0; i < length; i++)
                {
                    sb.Append((char)i);
                }

                Assert.Equal(sb.ToString(), sb.Intern());
                sb.Clear();
            }
        }
    }
}
