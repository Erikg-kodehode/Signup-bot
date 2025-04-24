using System;
using System.Linq;
using PublicHoliday;
using Xunit;
using Xunit.Abstractions;
namespace MorningSignInBot.Tests
{
    public class HolidayTests
    {
        private readonly NorwayPublicHoliday _norwayCalendar;

        public HolidayTests()
        {
            _norwayCalendar = new NorwayPublicHoliday();
        }

        [Fact]
        public void IsPublicHoliday_ChristmasDay_ReturnsTrue()
        {
            // Arrange
            DateTime christmasDay = new DateTime(2025, 12, 25);

            // Act
            bool isHoliday = _norwayCalendar.IsPublicHoliday(christmasDay);

            // Assert
            Assert.True(isHoliday, "Christmas day should be recognized as a holiday");
        }

        [Fact]
        public void IsPublicHoliday_NewYearsDay_ReturnsTrue()
        {
            // Arrange
            DateTime newYearsDay = new DateTime(2025, 1, 1);

            // Act
            bool isHoliday = _norwayCalendar.IsPublicHoliday(newYearsDay);

            // Assert
            Assert.True(isHoliday, "New Year's day should be recognized as a holiday");
        }

        [Fact]
        public void IsPublicHoliday_ConstitutionDay_ReturnsTrue()
        {
            // Arrange
            DateTime constitutionDay = new DateTime(2025, 5, 17);

            // Act
            bool isHoliday = _norwayCalendar.IsPublicHoliday(constitutionDay);

            // Assert
            Assert.True(isHoliday, "Norwegian Constitution day should be recognized as a holiday");
        }

        [Fact]
        public void IsPublicHoliday_RegularWorkDay_ReturnsFalse()
        {
            // Arrange
            DateTime regularWorkday = new DateTime(2025, 4, 28); // A Monday that's not a holiday

            // Act
            bool isHoliday = _norwayCalendar.IsPublicHoliday(regularWorkday);

            // Assert
            Assert.False(isHoliday, "Regular workday should not be recognized as a holiday");
        }

        [Fact]
        public void IsPublicHoliday_Weekend_ReturnsFalse()
        {
            // Arrange
            DateTime saturday = new DateTime(2025, 4, 26);
            DateTime sunday = new DateTime(2025, 4, 27);

            // Act & Assert
            Assert.False(_norwayCalendar.IsPublicHoliday(saturday), "Saturday is not a public holiday");
            Assert.False(_norwayCalendar.IsPublicHoliday(sunday), "Sunday is not a public holiday");
        }

        [Fact]
        public void GetHolidayName_ChristmasDay_ReturnsCorrectName()
        {
            // Arrange
            DateTime christmasDay = new DateTime(2025, 12, 25);

            // Act
            var holidays = _norwayCalendar.PublicHolidays(christmasDay.Year);
            var holiday = holidays.FirstOrDefault(h => h.Date == christmasDay);
            string holidayName = holiday.ToString();

            // Assert
            Assert.NotNull(holiday);
            Assert.Contains("Christmas", holidayName, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void PublicHolidays_For2025_ReturnsCorrectCount()
        {
            // Act
            var holidays = _norwayCalendar.PublicHolidays(2025);

            // Assert
            Assert.True(holidays.Count() >= 10, "There should be at least 10 public holidays in Norway for 2025");
        }
    }
}

