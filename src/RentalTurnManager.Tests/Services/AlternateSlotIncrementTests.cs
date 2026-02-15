using System;
using Xunit;
using RentalTurnManager.Lambda;

namespace RentalTurnManager.Tests.Services
{
    public class AlternateSlotIncrementTests
    {
        [Theory]
        // availableMinutes, originalIncrement, expected
        [InlineData(150, 30, 30)]   // 150/30 = 5 slots -> keep original
        [InlineData(100, 30, 20)]   // 100/30 = 3 -> desired = floor(100/5)=20
        [InlineData(10, 30, 5)]     // very small window -> min 5
        [InlineData(300, 30, 60)]   // large window -> desired=60
        [InlineData(125, 25, 25)]   // 125/25 = 5 -> keep original
        public void ComputeIncrementMinutes_ReturnsExpected(double availableMinutes, int originalIncrement, int expected)
        {
            var actual = Function.ComputeIncrementMinutes(availableMinutes, originalIncrement);
            Assert.Equal(expected, actual);
        }
    }
}
