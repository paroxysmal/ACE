using System.Linq;
using System.Threading;
using Microsoft.EntityFrameworkCore;

using log4net;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

using ACE.Database.Models.Auth;
using ACE.Entity.Enum;

namespace ACE.Database
{
    public class AuthenticationDatabase
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public bool Exists(bool retryUntilFound)
        {
            var config = Common.ConfigManager.Config.MySql.Authentication;

            for (; ; )
            {
                using (var context = new AuthDbContext())
                {
                    if (((RelationalDatabaseCreator)context.Database.GetService<IDatabaseCreator>()).Exists())
                    {
                        log.Debug($"Successfully connected to {config.Database} database on {config.Host}:{config.Port}.");
                        return true;
                    }
                }

                log.Error($"Attempting to reconnect to {config.Database} database on {config.Host}:{config.Port} in 5 seconds...");

                if (retryUntilFound)
                    Thread.Sleep(5000);
                else
                    return false;
            }
        }


        /// <exception cref="MySqlException">Account with name already exists.</exception>
        public Account CreateAccount(string name, string password, AccessLevel accessLevel)
        {
            var account = new Account();
            account.CreateRandomSalt();

            account.AccountName = name;
            account.SetPassword(password);
            account.AccessLevel = (uint)accessLevel;

            using (var context = new AuthDbContext())
            {
                context.Account.Add(account);

                context.SaveChanges();
            }

            return account;
        }

        /// <summary>
        /// Will return null if the accountId was not found.
        /// </summary>
        public Account GetAccountById(uint accountId)
        {
            using (var context = new AuthDbContext())
            {
                return context.Account
                    .AsNoTracking()
                    .FirstOrDefault(r => r.AccountId == accountId);
            }
        }

        /// <summary>
        /// Will return null if the accountName was not found.
        /// </summary>
        public Account GetAccountByName(string accountName)
        {
            using (var context = new AuthDbContext())
            {
                return context.Account
                    .AsNoTracking()
                    .FirstOrDefault(r => r.AccountName == accountName);
            }
        }

        /// <summary>
        /// id will be 0 if the accountName was not found.
        /// </summary>
        public uint GetAccountIdByName(string accountName)
        {
            using (var context = new AuthDbContext())
            {
                var result = context.Account
                    .AsNoTracking()
                    .FirstOrDefault(r => r.AccountName == accountName);

                return (result != null) ? result.AccountId : 0;
            }
        }

        public void UpdateAccount(Account account)
        {
            using (var context = new AuthDbContext())
            {
                context.Entry(account).State = EntityState.Modified;

                context.SaveChanges();
            }
        }

        public bool UpdateAccountAccessLevel(uint accountId, AccessLevel accessLevel)
        {
            using (var context = new AuthDbContext())
            {
                var account = context.Account
                    .First(r => r.AccountId == accountId);

                if (account == null)
                    return false;

                account.AccessLevel = (uint)accessLevel;

                context.SaveChanges();
            }

            return true;
        }
    }
}
