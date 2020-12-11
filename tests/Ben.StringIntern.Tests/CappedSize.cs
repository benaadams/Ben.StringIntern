using System;
using System.Linq;
using System.Text;

using Ben.Collections.Specialized;

using Xunit;

namespace Ben.StringIntern.Tests
{
    public class CappedSize
    {
        [Fact]
        void ItemsAboveCapAreRemoved_Ascending()
        {
            var pool = new InternPool(5, 5);

            for (int i = 0; i <= 125; i++)
            {
                string str = GetTestString(i);
                pool.Intern(str);
            }

            Assert.Equal(5, pool.Count);

            for (int i = 121; i <= 125; i++)
            {
                string str = GetTestString(i);

                Assert.Contains(str, pool);
            }
        }

        [Fact]
        void ItemsAboveCapAreRemoved_Descending()
        {
            var pool = new InternPool(5, 5);

            for (int i = 125; i >= 0; i--)
            {
                string str = GetTestString(i);

                pool.Intern(str);
            }

            Assert.Equal(5, pool.Count);

            for (int i = 5; i >= 0; i--)
            {
                string str = GetTestString(i);

                Assert.Contains(str, pool);
            }
        }

        [Fact]
        void ItemsAboveCapAreRemoved()
        {
            var pool = new InternPool(5, 32);

            for (int i = 0; i <= 125; i++)
            {
                string str = GetTestString(i);
                pool.Intern(str);
            }

            for (int i = 125; i >= (125 - 64); i=-2)
            {
                string str = GetTestString(i);
                pool.Intern(str);
            }

            Assert.Equal(32, pool.Count);

            for (int i = 125; i >= (125 - 64); i = -2)
            {
                string str = GetTestString(i);
                pool.Intern(str);

                Assert.Contains(str, pool);
            }

            for (int x = 0; x < 32; x++)
            {
                int i = x + (125 - 64);
                string str = GetTestString(i);
                pool.Intern(str);

                Assert.Contains(str, pool);
            }

            Assert.Equal(32, pool.Count);

            for (int x = 0; x < 32; x++)
            {
                int i = x + (125 - 64);
                string str = GetTestString(i);

                Assert.Contains(str, pool);
            }
        }

        private static string GetTestString(int i)
        {
            var array = Enumerable.Range(1, i).Select(x => (char)x).ToArray();

            // Prefix the array chars with the string count so they are easier to 
            // see on error/debug.
            if (i >= 100)
            {
                array[0] = (char)('0' + (i / 100));
                array[1] = (char)('0' + ((i % 100) / 10));
                array[2] = (char)('0' + (i % 10));
            }
            else if (i >= 10)
            {
                array[1] = (char)('0' + (i % 10));
                array[0] = (char)('0' + ((i % 100) / 10));
            }
            else if (i >= 1)
            {
                array[0] = (char)('0' + (i % 10));
            }

            return new(array);
        }
    }
}
