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
                var existing = pool.Intern(str);

                // Returned "same" value
                Assert.Equal(str, existing);
                // All new instances
                Assert.Same(existing, str);
                // Still there
                Assert.Contains(str, pool);
            }

            Assert.Equal(5, pool.Count);
            Assert.Equal(125, (int)pool.Added);
            // Considered is one higher as empty strings are not added
            Assert.Equal(126, (int)pool.Considered);
            // However they do count as deduped
            Assert.Equal(1, (int)pool.Deduped);

            for (int i = 121; i <= 125; i++)
            {
                string str = GetTestString(i);

                // Already there
                Assert.Contains(str, pool);

                var existing = pool.Intern(str);

                // Returned "same" value
                Assert.Equal(str, existing);
                // Returned prior instance
                Assert.NotSame(existing, str);
            }

            Assert.Equal(5, pool.Count);
            Assert.Equal(125, (int)pool.Added);
            // 5 extra have been considered 
            Assert.Equal(126 + 5, (int)pool.Considered);
            // 5 extra deduped
            Assert.Equal(6, (int)pool.Deduped);
        }

        [Fact]
        void ItemsAboveCapAreRemoved_Descending()
        {
            var pool = new InternPool(5, 5);

            for (int i = 125; i >= 0; i--)
            {
                string str = GetTestString(i);

                var existing = pool.Intern(str);

                // Returned "same" value
                Assert.Equal(str, existing);
                // All new instances
                Assert.Same(existing, str);
                // Still there
                Assert.Contains(str, pool);
            }

            Assert.Equal(5, pool.Count);
            Assert.Equal(125, (int)pool.Added);
            // Considered is one higher as empty strings are not added
            Assert.Equal(126, (int)pool.Considered);
            // However they do count as deduped
            Assert.Equal(1, (int)pool.Deduped);

            // Start at 1 as empty strings are always the same and not added
            for (int i = 1; i < 6; i++)
            {
                string str = GetTestString(i);

                // Already there
                Assert.Contains(str, pool);

                var existing = pool.Intern(str);

                // Returned "same" value
                Assert.Equal(str, existing);
                // Returned prior instance
                Assert.NotSame(existing, str);
            }

            Assert.Equal(5, pool.Count);
            Assert.Equal(125, (int)pool.Added);
            // 5 extra have been considered 
            Assert.Equal(126 + 5, (int)pool.Considered);
            // 5 extra deduped
            Assert.Equal(6, (int)pool.Deduped);
        }

        [Fact]
        void ItemsAboveCapAreRemoved()
        {
            var pool = new InternPool(5, 32);

            for (int i = 1; i <= 125; i++)
            {
                string str = GetTestString(i);
                var existing = pool.Intern(str);

                // Returned "same" value
                Assert.Equal(str, existing);
                // All new instances
                Assert.Same(existing, str);
                // Still there
                Assert.Contains(str, pool);
            }

            Assert.Equal(32, pool.Count);
            Assert.Equal(125, (int)pool.Added);
            Assert.Equal(125, (int)pool.Considered);
            Assert.Equal(0, (int)pool.Deduped);

            // Sequential
            for (int i = 0; i < 32; i++)
            {
                string str = GetTestString(125 - i);
                var existing = pool.Intern(str);

                // Returned "same" value
                Assert.Equal(str, existing);
                // All previous instances
                Assert.NotSame(existing, str);
                // Still there
                Assert.Contains(str, pool);
            }

            Assert.Equal(32, pool.Count);
            // No new added
            Assert.Equal(125, (int)pool.Added);
            // 32 extra checked
            Assert.Equal(125 + 32, (int)pool.Considered);
            // All deduped
            Assert.Equal(32, (int)pool.Deduped);

            // Skip one stepping
            for (int i = 0; i < 32; i += 2)
            {
                string str = GetTestString(125 - i);
                var existing = pool.Intern(str);

                // Returned "same" value
                Assert.Equal(str, existing);
                // All previous instances
                Assert.NotSame(existing, str);
                // Still there
                Assert.Contains(str, pool);
            }

            Assert.Equal(32, pool.Count);
            // No new added
            Assert.Equal(125, (int)pool.Added);
            // 16 extra checked
            Assert.Equal(125 + 32 + 16, (int)pool.Considered);
            // All deduped
            Assert.Equal(32 + 16, (int)pool.Deduped);

            for (int i = 0; i < 32; i += 2)
            {
                string str = GetTestString((125 - 32) - i);
                var existing = pool.Intern(str);

                // Returned "same" value
                Assert.Equal(str, existing);
                // All new instances
                Assert.Same(existing, str);
                // Still there
                Assert.Contains(str, pool);
            }

            Assert.Equal(32, pool.Count);
            // All new added
            Assert.Equal(125 + 16, (int)pool.Added);
            // 16 extra checked
            Assert.Equal(125 + 32 + 16 + 16, (int)pool.Considered);
            // Same deduped
            Assert.Equal(32 + 16, (int)pool.Deduped);
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
