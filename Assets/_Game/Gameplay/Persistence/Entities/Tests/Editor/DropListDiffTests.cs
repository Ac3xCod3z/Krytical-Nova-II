using KryticalNova.Persistence.Entities;
using NUnit.Framework;

namespace KryticalNova.Persistence.Entities.Tests
{
    /// <summary>M7.5 the drop list one player is told: a new drop once, a change (a partial take, a hold ending) again, a gone one once.</summary>
    public class DropListDiffTests
    {
        [Test]
        public void ADrop_IsToldOnce_AgainWhenItChanges_AndGoneOnceWhenItIsNotThere()
        {
            var list = new DropListDiff();
            Assert.IsTrue(list.Tell(7, 9001, 3, true), "a new drop");
            Assert.IsEmpty(list.EndLook());
            Assert.IsFalse(list.Tell(7, 9001, 3, true), "the same: not again");
            list.EndLook();
            Assert.IsTrue(list.Tell(7, 9001, 3, false), "its hold ended");
            list.EndLook();
            Assert.IsTrue(list.Tell(7, 9001, 1, false), "two were taken");
            list.EndLook();
            CollectionAssert.AreEqual(new[] { 7u }, list.EndLook(), "not seen in a look: gone");
            Assert.IsEmpty(list.EndLook(), "and told only once");
            Assert.IsTrue(list.Tell(7, 9001, 1, false), "an id seen again after it went is new again");
        }
    }
}
