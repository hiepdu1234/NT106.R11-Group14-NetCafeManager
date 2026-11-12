using NetCafeManager.Shared.Billing;

namespace NetCafeManager.Server.Billing;

public static class BalanceWarningEvaluator
{
    public static BalanceWarningState Evaluate(
        decimal balance,
        decimal hourlyRate,
        TimeSpan? warningWindow = null)
    {
        if (balance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(balance));
        }

        if (hourlyRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hourlyRate));
        }

        TimeSpan threshold = warningWindow ?? TimeSpan.FromMinutes(15);
        if (threshold <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(warningWindow));
        }

        double remainingHours = decimal.ToDouble(balance / hourlyRate);
        TimeSpan remaining = TimeSpan.FromHours(remainingHours);
        bool shouldWarn = remaining <= threshold;
        string message = shouldWarn
            ? "Tài khoản sắp hết tiền, vui lòng nạp thêm gói giờ."
            : "Số dư vẫn đủ cho phiên sử dụng hiện tại.";
        return new BalanceWarningState(shouldWarn, remaining, message);
    }
}
