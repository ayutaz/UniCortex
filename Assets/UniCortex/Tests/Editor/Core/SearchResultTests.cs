using System.Collections.Generic;
using NUnit.Framework;

namespace UniCortex.Tests.Editor.Core
{
    public class SearchResultTests
    {
        [Test]
        public void CompareTo_SmallerScore_IsNegative()
        {
            var a = new SearchResult { InternalId = 0, Score = 0.1f };
            var b = new SearchResult { InternalId = 1, Score = 0.5f };
            Assert.Less(a.CompareTo(b), 0);
        }

        [Test]
        public void CompareTo_LargerScore_IsPositive()
        {
            var a = new SearchResult { InternalId = 0, Score = 0.9f };
            var b = new SearchResult { InternalId = 1, Score = 0.1f };
            Assert.Greater(a.CompareTo(b), 0);
        }

        [Test]
        public void CompareTo_EqualScore_IsZero()
        {
            var a = new SearchResult { InternalId = 0, Score = 0.5f };
            var b = new SearchResult { InternalId = 1, Score = 0.5f };
            Assert.AreEqual(0, a.CompareTo(b));
        }

        [Test]
        public void Sort_AscendingByScore()
        {
            var list = new List<SearchResult>
            {
                new SearchResult { InternalId = 0, Score = 0.9f },
                new SearchResult { InternalId = 1, Score = 0.1f },
                new SearchResult { InternalId = 2, Score = 0.5f }
            };
            list.Sort();

            Assert.AreEqual(1, list[0].InternalId); // 0.1
            Assert.AreEqual(2, list[1].InternalId); // 0.5
            Assert.AreEqual(0, list[2].InternalId); // 0.9
        }
    }
}
