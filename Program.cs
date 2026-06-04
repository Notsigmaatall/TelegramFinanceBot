using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramFinanceBot
{
    public class Transaction
    {
        public int Id { get; set; }
        public long UserId { get; set; }
        public decimal Amount { get; set; }
        public string Type { get; set; } = "";
        public string Category { get; set; } = "";
        public string Description { get; set; } = "";
        public DateTime Date { get; set; }
    }

    public class Budget
    {
        public long UserId { get; set; }
        public string Category { get; set; } = "";
        public decimal BudgetLimit { get; set; }
        public int Month { get; set; }
        public int Year { get; set; }
    }

    public class UserSession
    {
        public long UserId { get; set; }
        public string CurrentStep { get; set; } = "idle";
        public decimal? PendingAmount { get; set; }
        public string PendingType { get; set; } = "";
        public string PendingDescription { get; set; } = "";
        public int? PendingEditId { get; set; }
    }

    public class DatabaseHelper
    {
        private string connectionString;

        public DatabaseHelper()
        {
            string dbPath = "finance.db";
            connectionString = $"Data Source={dbPath};Version=3;";
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                string sql = @"
                    CREATE TABLE IF NOT EXISTS Transactions (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        UserId INTEGER NOT NULL,
                        Amount REAL NOT NULL,
                        Type TEXT NOT NULL,
                        Category TEXT NOT NULL,
                        Description TEXT,
                        Date TEXT NOT NULL
                    );
                    
                    CREATE TABLE IF NOT EXISTS UserSessions (
                        UserId INTEGER PRIMARY KEY,
                        CurrentStep TEXT NOT NULL,
                        PendingAmount REAL,
                        PendingType TEXT,
                        PendingDescription TEXT,
                        PendingEditId INTEGER
                    );
                    
                    CREATE TABLE IF NOT EXISTS Budgets (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        UserId INTEGER NOT NULL,
                        Category TEXT NOT NULL,
                        BudgetLimit REAL NOT NULL,
                        Month INTEGER NOT NULL,
                        Year INTEGER NOT NULL,
                        UNIQUE(UserId, Category, Month, Year)
                    );
                    
                    CREATE TABLE IF NOT EXISTS DailyReports (
                        UserId INTEGER PRIMARY KEY,
                        LastReminderDate TEXT,
                        ReminderTime TEXT
                    );
                ";
                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.ExecuteNonQuery();
                }
            }
        }

        public void SaveTransaction(Transaction transaction)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                string sql = @"
                    INSERT INTO Transactions (UserId, Amount, Type, Category, Description, Date)
                    VALUES (@userId, @amount, @type, @category, @description, @date)
                ";
                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@userId", transaction.UserId);
                    command.Parameters.AddWithValue("@amount", (double)transaction.Amount);
                    command.Parameters.AddWithValue("@type", transaction.Type);
                    command.Parameters.AddWithValue("@category", transaction.Category);
                    command.Parameters.AddWithValue("@description", transaction.Description ?? "");
                    command.Parameters.AddWithValue("@date", transaction.Date.ToString("yyyy-MM-dd HH:mm:ss"));
                    command.ExecuteNonQuery();
                }
            }
        }

        public void UpdateTransaction(Transaction transaction)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                string sql = @"
                    UPDATE Transactions 
                    SET Amount = @amount, Type = @type, Category = @category, Description = @description
                    WHERE Id = @id AND UserId = @userId
                ";
                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@id", transaction.Id);
                    command.Parameters.AddWithValue("@userId", transaction.UserId);
                    command.Parameters.AddWithValue("@amount", (double)transaction.Amount);
                    command.Parameters.AddWithValue("@type", transaction.Type);
                    command.Parameters.AddWithValue("@category", transaction.Category);
                    command.Parameters.AddWithValue("@description", transaction.Description ?? "");
                    command.ExecuteNonQuery();
                }
            }
        }

        public Transaction GetTransactionById(int id, long userId)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                string sql = "SELECT * FROM Transactions WHERE Id = @id AND UserId = @userId";
                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@id", id);
                    command.Parameters.AddWithValue("@userId", userId);
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new Transaction
                            {
                                Id = reader.GetInt32(0),
                                UserId = reader.GetInt64(1),
                                Amount = Convert.ToDecimal(reader.GetDouble(2)),
                                Type = reader.GetString(3),
                                Category = reader.GetString(4),
                                Description = reader.GetString(5),
                                Date = DateTime.Parse(reader.GetString(6))
                            };
                        }
                    }
                }
            }
            return null;
        }

        public decimal GetBalance(long userId)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                string sql = @"
                    SELECT 
                        COALESCE(SUM(CASE WHEN Type = 'income' THEN Amount ELSE 0 END), 0) -
                        COALESCE(SUM(CASE WHEN Type = 'expense' THEN Amount ELSE 0 END), 0) as Balance
                    FROM Transactions WHERE UserId = @userId
                ";
                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@userId", userId);
                    var result = command.ExecuteScalar();
                    return result != DBNull.Value ? Convert.ToDecimal(result) : 0;
                }
            }
        }

        public List<Transaction> GetTransactions(long userId, DateTime startDate, DateTime endDate)
        {
            var transactions = new List<Transaction>();
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                string sql = @"
                    SELECT * FROM Transactions 
                    WHERE UserId = @userId AND Date BETWEEN @startDate AND @endDate
                    ORDER BY Date DESC
                ";
                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@userId", userId);
                    command.Parameters.AddWithValue("@startDate", startDate.ToString("yyyy-MM-dd HH:mm:ss"));
                    command.Parameters.AddWithValue("@endDate", endDate.ToString("yyyy-MM-dd HH:mm:ss"));

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            transactions.Add(new Transaction
                            {
                                Id = reader.GetInt32(0),
                                UserId = reader.GetInt64(1),
                                Amount = Convert.ToDecimal(reader.GetDouble(2)),
                                Type = reader.GetString(3),
                                Category = reader.GetString(4),
                                Description = reader.GetString(5),
                                Date = DateTime.Parse(reader.GetString(6))
                            });
                        }
                    }
                }
            }
            return transactions;
        }

        public decimal GetCategoryExpense(long userId, string category, int month, int year)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                string sql = @"
                    SELECT COALESCE(SUM(Amount), 0) FROM Transactions 
                    WHERE UserId = @userId 
                    AND Type = 'expense' 
                    AND Category = @category 
                    AND strftime('%m', Date) = @month 
                    AND strftime('%Y', Date) = @year
                ";
                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@userId", userId);
                    command.Parameters.AddWithValue("@category", category);
                    command.Parameters.AddWithValue("@month", month.ToString("D2"));
                    command.Parameters.AddWithValue("@year", year.ToString());
                    var result = command.ExecuteScalar();
                    return result != DBNull.Value ? Convert.ToDecimal(result) : 0;
                }
            }
        }

        public void SetBudget(long userId, string category, decimal limit)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                string sql = @"
                    INSERT OR REPLACE INTO Budgets (UserId, Category, BudgetLimit, Month, Year)
                    VALUES (@userId, @category, @limit, @month, @year)
                ";
                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@userId", userId);
                    command.Parameters.AddWithValue("@category", category);
                    command.Parameters.AddWithValue("@limit", (double)limit);
                    command.Parameters.AddWithValue("@month", DateTime.Now.Month);
                    command.Parameters.AddWithValue("@year", DateTime.Now.Year);
                    command.ExecuteNonQuery();
                }
            }
        }

        public Budget GetBudget(long userId, string category)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                string sql = @"
                    SELECT * FROM Budgets 
                    WHERE UserId = @userId AND Category = @category AND Month = @month AND Year = @year
                ";
                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@userId", userId);
                    command.Parameters.AddWithValue("@category", category);
                    command.Parameters.AddWithValue("@month", DateTime.Now.Month);
                    command.Parameters.AddWithValue("@year", DateTime.Now.Year);
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new Budget
                            {
                                UserId = reader.GetInt64(1),
                                Category = reader.GetString(2),
                                BudgetLimit = Convert.ToDecimal(reader.GetDouble(3)),
                                Month = reader.GetInt32(4),
                                Year = reader.GetInt32(5)
                            };
                        }
                    }
                }
            }
            return null;
        }

        public List<Budget> GetAllBudgets(long userId)
        {
            var budgets = new List<Budget>();
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                string sql = @"
                    SELECT * FROM Budgets 
                    WHERE UserId = @userId AND Month = @month AND Year = @year
                ";
                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@userId", userId);
                    command.Parameters.AddWithValue("@month", DateTime.Now.Month);
                    command.Parameters.AddWithValue("@year", DateTime.Now.Year);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            budgets.Add(new Budget
                            {
                                UserId = reader.GetInt64(1),
                                Category = reader.GetString(2),
                                BudgetLimit = Convert.ToDecimal(reader.GetDouble(3)),
                                Month = reader.GetInt32(4),
                                Year = reader.GetInt32(5)
                            });
                        }
                    }
                }
            }
            return budgets;
        }

        public void SaveSession(UserSession session)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                string sql = @"
                    INSERT OR REPLACE INTO UserSessions (UserId, CurrentStep, PendingAmount, PendingType, PendingDescription, PendingEditId)
                    VALUES (@userId, @currentStep, @pendingAmount, @pendingType, @pendingDescription, @pendingEditId)
                ";
                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@userId", session.UserId);
                    command.Parameters.AddWithValue("@currentStep", session.CurrentStep);
                    command.Parameters.AddWithValue("@pendingAmount", session.PendingAmount.HasValue ? (object)(double)session.PendingAmount.Value : DBNull.Value);
                    command.Parameters.AddWithValue("@pendingType", session.PendingType ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@pendingDescription", session.PendingDescription ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@pendingEditId", session.PendingEditId ?? (object)DBNull.Value);
                    command.ExecuteNonQuery();
                }
            }
        }

        public UserSession LoadSession(long userId)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                string sql = "SELECT CurrentStep, PendingAmount, PendingType, PendingDescription, PendingEditId FROM UserSessions WHERE UserId = @userId";
                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@userId", userId);
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new UserSession
                            {
                                UserId = userId,
                                CurrentStep = reader.GetString(0),
                                PendingAmount = reader.IsDBNull(1) ? null : (decimal?)reader.GetDouble(1),
                                PendingType = reader.IsDBNull(2) ? null : reader.GetString(2),
                                PendingDescription = reader.IsDBNull(3) ? null : reader.GetString(3),
                                PendingEditId = reader.IsDBNull(4) ? null : (int?)reader.GetInt32(4)
                            };
                        }
                    }
                }
            }
            return new UserSession { UserId = userId, CurrentStep = "idle" };
        }

        public void ClearSession(long userId)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                string sql = "DELETE FROM UserSessions WHERE UserId = @userId";
                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@userId", userId);
                    command.ExecuteNonQuery();
                }
            }
        }

        public void SaveDailyReminder(long userId, string reminderTime)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                string sql = @"
                    INSERT OR REPLACE INTO DailyReports (UserId, LastReminderDate, ReminderTime)
                    VALUES (@userId, @lastDate, @reminderTime)
                ";
                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@userId", userId);
                    command.Parameters.AddWithValue("@lastDate", DateTime.Today.ToString("yyyy-MM-dd"));
                    command.Parameters.AddWithValue("@reminderTime", reminderTime);
                    command.ExecuteNonQuery();
                }
            }
        }

        public string GetReminderTime(long userId)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                string sql = "SELECT ReminderTime FROM DailyReports WHERE UserId = @userId";
                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@userId", userId);
                    var result = command.ExecuteScalar();
                    return result?.ToString();
                }
            }
        }

        public bool WasRemindedToday(long userId)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                string sql = "SELECT LastReminderDate FROM DailyReports WHERE UserId = @userId";
                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@userId", userId);
                    var result = command.ExecuteScalar();
                    return result?.ToString() == DateTime.Today.ToString("yyyy-MM-dd");
                }
            }
        }

        public void UpdateReminderDate(long userId)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                string sql = "UPDATE DailyReports SET LastReminderDate = @date WHERE UserId = @userId";
                using (var command = new SQLiteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@date", DateTime.Today.ToString("yyyy-MM-dd"));
                    command.Parameters.AddWithValue("@userId", userId);
                    command.ExecuteNonQuery();
                }
            }
        }
    }

    public class FinanceBot
    {
        private ITelegramBotClient botClient;
        private DatabaseHelper db;
        private Dictionary<long, UserSession> sessions;
        private Timer reminderTimer;
        private Dictionary<string, string> categoryNames;

        public FinanceBot(string token)
        {
            botClient = new TelegramBotClient(token);
            db = new DatabaseHelper();
            sessions = new Dictionary<long, UserSession>();
            categoryNames = new Dictionary<string, string>
            {
                // Расходы
                {"food", "🍔 Еда"},
                {"transport", "🚗 Транспорт"},
                {"housing", "🏠 Жильё"},
                {"communication", "📱 Связь/Интернет"},
                {"entertainment", "🎉 Развлечения"},
                {"health", "💊 Здоровье"},
                {"education", "📚 Образование"},
                {"other", "💵 Другое (расход)"},
                // Доходы
                {"salary", "💰 Зарплата"},
                {"freelance", "💼 Фриланс/Подработка"},
                {"gift", "🎁 Подарок"},
                {"bonus", "📈 Кэшбэк/Бонусы"},
                {"debt_return", "💸 Возврат долга"},
                {"interest", "🏦 Проценты по вкладу"},
                {"other_income", "💵 Другое (доход)"}
            };
            StartReminderChecker();
        }

        private void StartReminderChecker()
        {
            reminderTimer = new Timer(async _ => await CheckReminders(), null, TimeSpan.Zero, TimeSpan.FromMinutes(60));
        }

        private async Task CheckReminders()
        {
            Console.WriteLine($"[{DateTime.Now}] Проверка напоминаний...");
        }

        public async Task SendDailyReminder(long chatId)
        {
            if (!db.WasRemindedToday(chatId))
            {
                await botClient.SendMessage(
                    chatId: chatId,
                    text: "📝 **Ежедневное напоминание!**\n\nНе забудьте записать свои траты за сегодня.\n\nИспользуйте `/add` чтобы добавить транзакцию или `/report` для просмотра отчёта.",
                    parseMode: ParseMode.Markdown);
                db.UpdateReminderDate(chatId);
            }
        }

        public async Task StartAsync(CancellationToken ct)
        {
            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = Array.Empty<UpdateType>()
            };

            botClient.StartReceiving(HandleUpdateAsync, HandleErrorAsync, receiverOptions, ct);
            Console.WriteLine("Бот запущен!");
        }

        private async Task HandleUpdateAsync(ITelegramBotClient client, Update update, CancellationToken ct)
        {
            if (update.CallbackQuery != null)
            {
                await HandleCallbackAsync(update.CallbackQuery);
                return;
            }

            if (update.Message is not { } message)
                return;

            var chatId = message.Chat.Id;
            var text = message.Text?.Trim();
            var username = message.From?.Username ?? "пользователь";

            if (!sessions.ContainsKey(chatId))
            {
                sessions[chatId] = db.LoadSession(chatId);
            }
            var session = sessions[chatId];

            if (text != null && text.StartsWith("/"))
            {
                await HandleCommand(chatId, text, username, session, ct);
                return;
            }

            await HandleDialog(chatId, text, session, ct);
        }

        private async Task HandleCommand(long chatId, string command, string username, UserSession session, CancellationToken ct)
        {
            var parts = command.Split(' ');
            var mainCommand = parts[0];

            switch (mainCommand)
            {
                case "/start":
                    session.CurrentStep = "idle";
                    db.SaveSession(session);
                    await botClient.SendMessage(chatId,
                        $"Привет, {username}! 👋\n\n" +
                        "Я бот для учёта личных финансов.\n\n" +
                        "📌 Команды:\n" +
                        "/add - добавить доход или расход\n" +
                        "/balance - показать баланс\n" +
                        "/report - отчёт за сегодня\n" +
                        "/week - отчёт за неделю\n" +
                        "/month - отчёт за месяц\n" +
                        "/export - экспорт отчёта в CSV\n" +
                        "/budget - установить бюджет на месяц\n" +
                        "/checkbudget - проверить бюджет\n" +
                        "/edit [ID] [сумма] - редактировать транзакцию\n" +
                        "/setreminder [ЧЧ:ММ] - установить ежедневное напоминание\n" +
                        "/cancel - отменить действие\n\n" +
                        "💰 Просто нажми /add и следуй инструкциям!",
                        cancellationToken: ct);
                    break;

                case "/add":
                    session.CurrentStep = "awaiting_amount";
                    session.PendingAmount = null;
                    session.PendingType = "";
                    session.PendingDescription = "";
                    session.PendingEditId = null;
                    db.SaveSession(session);
                    await botClient.SendMessage(chatId, "💰 Введите сумму (например: 500 или 123.50):", cancellationToken: ct);
                    break;

                case "/balance":
                    var balance = db.GetBalance(chatId);
                    await botClient.SendMessage(chatId,
                        $"💰 Ваш текущий баланс: {balance:F2} ₽\n" +
                        (balance >= 0 ? "✅ Отлично!" : "⚠️ У вас расходы превышают доходы"),
                        cancellationToken: ct);
                    break;

                case "/report":
                    await ShowReport(chatId, DateTime.Today, DateTime.Today, "за сегодня", ct);
                    break;

                case "/week":
                    var weekAgo = DateTime.Today.AddDays(-7);
                    await ShowReport(chatId, weekAgo, DateTime.Today, "за неделю", ct);
                    break;

                case "/month":
                    var monthAgo = DateTime.Today.AddMonths(-1);
                    await ShowReport(chatId, monthAgo, DateTime.Today, "за месяц", ct);
                    break;

                case "/export":
                    await ExportToCsv(chatId, DateTime.Today.AddMonths(-1), DateTime.Today, ct);
                    break;

                case "/budget":
                    if (parts.Length < 3)
                    {
                        await botClient.SendMessage(chatId, "📊 Использование: `/budget [категория] [сумма]`\n\nПример: `/budget Еда 15000`\n\nКатегории: Еда, Транспорт, Жильё, Связь/Интернет, Развлечения, Здоровье, Образование, Другое", parseMode: ParseMode.Markdown, cancellationToken: ct);
                        return;
                    }
                    var categoryName = string.Join(" ", parts.Skip(1).Take(parts.Length - 2));
                    if (decimal.TryParse(parts.Last(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal limit))
                    {
                        var fullCategory = categoryNames.FirstOrDefault(x => x.Value == categoryName || x.Key == categoryName).Value ?? categoryName;
                        db.SetBudget(chatId, fullCategory, limit);
                        await botClient.SendMessage(chatId, $"✅ Установлен бюджет на категорию {fullCategory}: {limit:F2} ₽ на текущий месяц.", cancellationToken: ct);
                    }
                    else
                    {
                        await botClient.SendMessage(chatId, "❌ Неверный формат. Пример: `/budget Еда 15000`", parseMode: ParseMode.Markdown, cancellationToken: ct);
                    }
                    break;

                case "/checkbudget":
                    await CheckBudget(chatId, ct);
                    break;

                case "/edit":
                    if (parts.Length < 3)
                    {
                        await botClient.SendMessage(chatId, "✏️ Использование: `/edit [ID] [новая сумма]`\n\nЧтобы узнать ID транзакции, используйте `/report`", parseMode: ParseMode.Markdown, cancellationToken: ct);
                        return;
                    }
                    if (int.TryParse(parts[1], out int editId) && decimal.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out decimal newAmount))
                    {
                        var existingTransaction = db.GetTransactionById(editId, chatId);
                        if (existingTransaction != null)
                        {
                            existingTransaction.Amount = newAmount;
                            db.UpdateTransaction(existingTransaction);
                            await botClient.SendMessage(chatId, $"✅ Транзакция #{editId} обновлена! Новая сумма: {newAmount:F2} ₽", cancellationToken: ct);
                        }
                        else
                        {
                            await botClient.SendMessage(chatId, $"❌ Транзакция #{editId} не найдена.", cancellationToken: ct);
                        }
                    }
                    else
                    {
                        await botClient.SendMessage(chatId, "❌ Неверный формат. Пример: `/edit 5 1000`", parseMode: ParseMode.Markdown, cancellationToken: ct);
                    }
                    break;

                case "/setreminder":
                    if (parts.Length < 2)
                    {
                        await botClient.SendMessage(chatId, "⏰ Использование: `/setreminder [ЧЧ:ММ]`\n\nПример: `/setreminder 21:00`\n\nЯ буду напоминать вам записывать траты каждый день в это время.", parseMode: ParseMode.Markdown, cancellationToken: ct);
                        return;
                    }
                    var reminderTime = parts[1];
                    if (TimeSpan.TryParse(reminderTime, out _))
                    {
                        db.SaveDailyReminder(chatId, reminderTime);
                        await botClient.SendMessage(chatId, $"✅ Ежедневное напоминание установлено на {reminderTime}!", cancellationToken: ct);
                    }
                    else
                    {
                        await botClient.SendMessage(chatId, "❌ Неверный формат времени. Используйте ЧЧ:ММ (например, 21:00)", cancellationToken: ct);
                    }
                    break;

                case "/cancel":
                    session.CurrentStep = "idle";
                    db.SaveSession(session);
                    await botClient.SendMessage(chatId, "❌ Действие отменено.", cancellationToken: ct);
                    break;

                default:
                    await botClient.SendMessage(chatId, "❓ Неизвестная команда. Используйте /start для списка команд.", cancellationToken: ct);
                    break;
            }
        }

        private async Task CheckBudget(long chatId, CancellationToken ct)
        {
            var budgets = db.GetAllBudgets(chatId);
            if (budgets.Count == 0)
            {
                await botClient.SendMessage(chatId, "📊 У вас нет установленных бюджетов. Используйте `/budget [категория] [сумма]`", parseMode: ParseMode.Markdown, cancellationToken: ct);
                return;
            }

            var now = DateTime.Now;
            var report = "📊 **Отчёт по бюджетам на текущий месяц:**\n\n";
            var overBudget = false;

            foreach (var budget in budgets)
            {
                var spent = db.GetCategoryExpense(chatId, budget.Category, now.Month, now.Year);
                var percent = (double)(spent / budget.BudgetLimit) * 100;
                var progressBar = GetProgressBar(percent);
                report += $"{budget.Category}:\n";
                report += $"  Лимит: {budget.BudgetLimit:F2} ₽\n";
                report += $"  Потрачено: {spent:F2} ₽ ({percent:F1}%)\n";
                report += $"  {progressBar}\n";

                if (spent > budget.BudgetLimit)
                {
                    overBudget = true;
                    report += $"  ⚠️ **ПРЕВЫШЕНИЕ!** +{(spent - budget.BudgetLimit):F2} ₽\n";
                }
                else if (spent > budget.BudgetLimit * 0.9m)
                {
                    report += $"  ⚠️ Осталось: {(budget.BudgetLimit - spent):F2} ₽ (меньше 10%)\n";
                }
                report += "\n";
            }

            if (overBudget)
            {
                report += "⚠️ Внимание! У вас превышение бюджета по некоторым категориям!";
            }

            await botClient.SendMessage(chatId, report, parseMode: ParseMode.Markdown, cancellationToken: ct);
        }

        private string GetProgressBar(double percent)
        {
            var filled = (int)(percent / 10);
            var empty = 10 - filled;
            var bar = new string('█', Math.Min(filled, 10)) + new string('░', empty);
            return bar;
        }

        private async Task ExportToCsv(long chatId, DateTime startDate, DateTime endDate, CancellationToken ct)
        {
            var transactions = db.GetTransactions(chatId, startDate, endDate.AddDays(1).AddSeconds(-1));

            if (transactions.Count == 0)
            {
                await botClient.SendMessage(chatId, "📊 Нет транзакций за указанный период для экспорта.", cancellationToken: ct);
                return;
            }

            var csv = new StringBuilder();
            csv.AppendLine("ID,Дата,Сумма,Тип,Категория,Описание");

            foreach (var t in transactions)
            {
                csv.AppendLine($"{t.Id},{t.Date:yyyy-MM-dd HH:mm:ss},{t.Amount:F2},{t.Type},{t.Category},{t.Description}");
            }

            var fileName = $"report_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            var fileBytes = Encoding.UTF8.GetBytes(csv.ToString());

            using var stream = new MemoryStream(fileBytes);
            var inputFile = new InputFileStream(stream, fileName);

            await botClient.SendDocument(chatId, inputFile, caption: $"📊 Отчёт за период {startDate:dd.MM.yyyy} - {endDate:dd.MM.yyyy}", cancellationToken: ct);
        }

        private async Task HandleDialog(long chatId, string text, UserSession session, CancellationToken ct)
        {
            if (session.CurrentStep == "idle")
            {
                await botClient.SendMessage(chatId, "Используйте /add чтобы добавить транзакцию или /balance для проверки баланса", cancellationToken: ct);
                return;
            }

            if (session.CurrentStep == "awaiting_amount")
            {
                if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal amount))
                {
                    if (amount <= 0)
                    {
                        await botClient.SendMessage(chatId, "❌ Сумма должна быть больше 0. Попробуйте ещё раз:", cancellationToken: ct);
                        return;
                    }

                    session.PendingAmount = amount;
                    session.CurrentStep = "awaiting_type";
                    db.SaveSession(session);

                    var keyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("💰 Доход", "type_income") },
                        new[] { InlineKeyboardButton.WithCallbackData("💸 Расход", "type_expense") }
                    });

                    await botClient.SendMessage(chatId, $"Сумма: {amount:F2} ₽\nТеперь выберите тип:", replyMarkup: keyboard, cancellationToken: ct);
                }
                else
                {
                    await botClient.SendMessage(chatId, "❌ Введите корректное число (например: 500 или 123.50):", cancellationToken: ct);
                }
                return;
            }
        }

        private async Task HandleCallbackAsync(CallbackQuery callbackQuery)
        {
            var chatId = callbackQuery.Message.Chat.Id;
            var data = callbackQuery.Data;

            if (!sessions.ContainsKey(chatId))
            {
                sessions[chatId] = db.LoadSession(chatId);
            }
            var session = sessions[chatId];

            if (data.StartsWith("type_"))
            {
                var type = data.Replace("type_", "");
                session.PendingType = type;
                session.CurrentStep = "awaiting_category";
                db.SaveSession(session);

                InlineKeyboardMarkup keyboard;

                if (type == "income")
                {
                    // Категории для ДОХОДОВ
                    keyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("💰 Зарплата", "cat_salary") },
                        new[] { InlineKeyboardButton.WithCallbackData("💼 Фриланс/Подработка", "cat_freelance") },
                        new[] { InlineKeyboardButton.WithCallbackData("🎁 Подарок", "cat_gift") },
                        new[] { InlineKeyboardButton.WithCallbackData("📈 Кэшбэк/Бонусы", "cat_bonus") },
                        new[] { InlineKeyboardButton.WithCallbackData("💸 Возврат долга", "cat_debt_return") },
                        new[] { InlineKeyboardButton.WithCallbackData("🏦 Проценты по вкладу", "cat_interest") },
                        new[] { InlineKeyboardButton.WithCallbackData("💵 Другое", "cat_other_income") }
                    });
                }
                else
                {
                    // Категории для РАСХОДОВ
                    keyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("🍔 Еда", "cat_food") },
                        new[] { InlineKeyboardButton.WithCallbackData("🚗 Транспорт", "cat_transport") },
                        new[] { InlineKeyboardButton.WithCallbackData("🏠 Жильё", "cat_housing") },
                        new[] { InlineKeyboardButton.WithCallbackData("📱 Связь/Интернет", "cat_communication") },
                        new[] { InlineKeyboardButton.WithCallbackData("🎉 Развлечения", "cat_entertainment") },
                        new[] { InlineKeyboardButton.WithCallbackData("💊 Здоровье", "cat_health") },
                        new[] { InlineKeyboardButton.WithCallbackData("📚 Образование", "cat_education") },
                        new[] { InlineKeyboardButton.WithCallbackData("💵 Другое", "cat_other") }
                    });
                }

                await botClient.SendMessage(chatId,
                    $"Тип: {(type == "income" ? "Доход" : "Расход")}\nВыберите категорию:",
                    replyMarkup: keyboard);
                await botClient.AnswerCallbackQuery(callbackQuery.Id);
                return;
            }

            if (data.StartsWith("cat_"))
            {
                var category = data.Replace("cat_", "");

                var categoryNamesLocal = new Dictionary<string, string>
                {
                    // Расходы
                    {"food", "🍔 Еда"},
                    {"transport", "🚗 Транспорт"},
                    {"housing", "🏠 Жильё"},
                    {"communication", "📱 Связь/Интернет"},
                    {"entertainment", "🎉 Развлечения"},
                    {"health", "💊 Здоровье"},
                    {"education", "📚 Образование"},
                    {"other", "💵 Другое (расход)"},
                    // Доходы
                    {"salary", "💰 Зарплата"},
                    {"freelance", "💼 Фриланс/Подработка"},
                    {"gift", "🎁 Подарок"},
                    {"bonus", "📈 Кэшбэк/Бонусы"},
                    {"debt_return", "💸 Возврат долга"},
                    {"interest", "🏦 Проценты по вкладу"},
                    {"other_income", "💵 Другое (доход)"}
                };

                var categoryName = categoryNamesLocal.ContainsKey(category) ? categoryNamesLocal[category] : category;

                var transaction = new Transaction
                {
                    UserId = chatId,
                    Amount = session.PendingAmount.Value,
                    Type = session.PendingType,
                    Category = categoryName,
                    Description = session.PendingDescription ?? "",
                    Date = DateTime.Now
                };

                db.SaveTransaction(transaction);
                var balance = db.GetBalance(chatId);

                if (transaction.Type == "expense")
                {
                    var budget = db.GetBudget(chatId, categoryName);
                    if (budget != null)
                    {
                        var spent = db.GetCategoryExpense(chatId, categoryName, DateTime.Now.Month, DateTime.Now.Year);
                        if (spent > budget.BudgetLimit)
                        {
                            await botClient.SendMessage(chatId, $"⚠️ **Внимание!** Превышен бюджет на категорию {categoryName}!\nЛимит: {budget.BudgetLimit:F2} ₽\nПотрачено: {spent:F2} ₽", parseMode: ParseMode.Markdown);
                        }
                        else if (spent > budget.BudgetLimit * 0.9m)
                        {
                            var remaining = budget.BudgetLimit - spent;
                            await botClient.SendMessage(chatId, $"⚠️ Бюджет на категорию {categoryName} почти исчерпан!\nОсталось: {remaining:F2} ₽", parseMode: ParseMode.Markdown);
                        }
                    }
                }

                session.CurrentStep = "idle";
                db.ClearSession(chatId);
                sessions.Remove(chatId);

                var typeText = session.PendingType == "income" ? "Доход" : "Расход";
                await botClient.SendMessage(chatId,
                    $"✅ {typeText} добавлен!\n" +
                    $"Сумма: {transaction.Amount:F2} ₽\n" +
                    $"Категория: {categoryName}\n" +
                    $"💰 Новый баланс: {balance:F2} ₽");

                await botClient.AnswerCallbackQuery(callbackQuery.Id);
            }
        }

        private async Task ShowReport(long chatId, DateTime startDate, DateTime endDate, string periodName, CancellationToken ct)
        {
            var transactions = db.GetTransactions(chatId, startDate, endDate.AddDays(1).AddSeconds(-1));

            if (transactions.Count == 0)
            {
                await botClient.SendMessage(chatId, $"📊 Нет транзакций {periodName}.", cancellationToken: ct);
                return;
            }

            decimal totalIncome = 0;
            decimal totalExpense = 0;
            var report = $"📊 Отчёт {periodName}:\n\n";
            report += $"📅 Период: {startDate:dd.MM.yyyy} - {endDate:dd.MM.yyyy}\n";
            report += "━━━━━━━━━━━━━━━━━━━━\n";

            var recentTransactions = transactions.Take(10);
            foreach (var t in recentTransactions)
            {
                var sign = t.Type == "income" ? "+" : "-";
                report += $"{t.Date:dd.MM HH:mm} {sign}{t.Amount:F2} ₽ ({t.Category})\n";
                if (t.Type == "income") totalIncome += t.Amount;
                else totalExpense += t.Amount;
            }

            if (transactions.Count > 10)
            {
                report += $"\n...и ещё {transactions.Count - 10} транзакций\n";
            }

            report += "━━━━━━━━━━━━━━━━━━━━\n";
            report += $"📈 Доходы: +{totalIncome:F2} ₽\n";
            report += $"📉 Расходы: -{totalExpense:F2} ₽\n";
            report += $"💰 Баланс: {(totalIncome - totalExpense):F2} ₽\n";
            report += "\n━━━━━━━━━━━━━━━━━━━━\n";
            report += "✏️ Для редактирования транзакции используйте:\n/edit [ID] [новая сумма]\n\nПример: /edit 1 500";

            await botClient.SendMessage(chatId, report, cancellationToken: ct);
        }

        private Task HandleErrorAsync(ITelegramBotClient client, Exception exception, CancellationToken ct)
        {
            Console.WriteLine($"Ошибка: {exception.Message}");
            return Task.CompletedTask;
        }
    }

    class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Telegram Бот для учёта финансов ===\n");

        string token = Environment.GetEnvironmentVariable("BOT_TOKEN");
        if (string.IsNullOrEmpty(token))
        {
            Console.Write("Введите токен бота (получите у @BotFather): ");
            token = Console.ReadLine()?.Trim();
        }

        if (string.IsNullOrEmpty(token))
        {
            Console.WriteLine("Токен не может быть пустым!");
            return;
        }

        var bot = new FinanceBot(token);
        var cts = new CancellationTokenSource();

        await bot.StartAsync(cts.Token);

        // На сервере не ждём Enter, а держим бота запущенным
        // На локальном компьютере ждём Enter для выхода
        if (Environment.GetEnvironmentVariable("RENDER") == null)
        {
            Console.WriteLine("Нажми Enter для выхода...");
            Console.ReadLine();
        }
        else
        {
            // На Render просто ждём бесконечно
            await Task.Delay(-1, cts.Token);
        }

        cts.Cancel();
    }
}
}
