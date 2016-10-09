﻿using System;

namespace Wallet.Domain
{
	public class TransactionItem
	{
		public Decimal Amount { get; set; }
		public CurrencyEnum Currency { get; set; }
		public DirectionEnum Direction { get; set; }
		public DateTime Date { get; set; }
		public String To { get; set; }
		public String Id { get; set; }
		public Decimal Fee { get; set; }
		public TransactionItem PreviousTransactionItem { get; set; }
		public Decimal RunningBalance { get 
			{ 
				return (Decimal)0.99;
			}
		}

		public TransactionItem(Decimal Amount, DirectionEnum Direction, CurrencyEnum Currency, DateTime Date, String To, String Id, Decimal Fee) {
			this.Amount = Amount;
			this.Direction = Direction;
			this.Currency = Currency;
			this.Date = Date;
			this.To = To;
			this.Id = Id;
			this.Fee = Fee;
		}
	}
}