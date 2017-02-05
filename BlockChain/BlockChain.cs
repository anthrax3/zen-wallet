using System;
using Consensus;
using BlockChain.Store;
using Store;
using Infrastructure;
using System.Collections.Generic;
using Microsoft.FSharp.Collections;
using System.Linq;
using BlockChain.Data;
using System.Collections.Concurrent;

namespace BlockChain
{
	public class BlockChain : ResourceOwner
	{
		private readonly TimeSpan OLD_TIP_TIME_SPAN = TimeSpan.FromMinutes(5);
		private readonly DBContext _DBContext;
		private readonly ConcurrentStack<Types.Block> _OrphansActions;

		public TxMempool TxMempool { get; private set; }
		public UTXOStore UTXOStore { get; private set; }
		public BlockStore BlockStore { get; private set; }
		public ContractStore ContractStore { get; private set; }
		public BlockNumberDifficulties BlockNumberDifficulties { get; private set; }
		public ChainTip ChainTip { get; private set; }
		public BlockTimestamps Timestamps { get; private set; }
		public byte[] GenesisBlockHash { get; private set; }
		private object _lockObject = new Object();

		public TransactionValidation.PointedTransaction GetPointedTransaction(TransactionContext dbTx, Types.Transaction tx)
		{
			var outputs = new List<Types.Output>();

			foreach (var input in tx.inputs)
			{
				FSharpList<Types.Output> _outputs = null;

				if (TxMempool.ContainsKey(input.txHash))
				{
					_outputs = TxMempool.Get(input.txHash).outputs;
				}
				else if (BlockStore.TxStore.ContainsKey(dbTx, input.txHash))
				{
					_outputs = BlockStore.TxStore.Get(dbTx, input.txHash).Value.outputs;
				}

				outputs.Add(_outputs[(int)input.index]);
			}

			return TransactionValidation.toPointedTransaction(
				tx,
				ListModule.OfSeq<Types.Output>(outputs)
			);
		}

		public bool IsTipOld //TODO: consider caching
		{
			get
			{
				var tipBlock = Tip;

				if (tipBlock == null)
				{
					return true;
				}
				else 
				{
					DateTime tipDateTime = DateTime.FromBinary(tipBlock.Value.header.timestamp);
					TimeSpan diff = DateTime.Now - tipDateTime;

					return diff > OLD_TIP_TIME_SPAN;
				}
			}
		}

		//TODO: refactor
		public Keyed<Types.Block> Tip { get; set; }

		public BlockChain(string dbName, byte[] genesisBlockHash) {
			_DBContext = new DBContext(dbName);
			TxMempool = new TxMempool();
			UTXOStore = new UTXOStore();
			BlockStore = new BlockStore();
			ContractStore = new ContractStore();
			BlockNumberDifficulties = new BlockNumberDifficulties();
			ChainTip = new ChainTip();
			Timestamps = new BlockTimestamps();
			_OrphansActions = new ConcurrentStack<Types.Block>();
            GenesisBlockHash = genesisBlockHash;// GetGenesisBlock().Key;

			OwnResource(_DBContext);

			using (var context = _DBContext.GetTransactionContext())
			{
				var chainTip = ChainTip.Context(context).Value;

				//TODO: check if makred as main?
				Tip = chainTip == null ? null : BlockStore.GetBlock(context, chainTip);
			}

			InitBlockTimestamps();
		}

		public void InitBlockTimestamps()
		{
			if (Tip != null)
			{
				var timestamps = new List<long>();
				var itr = Tip == null ? null : Tip.Value;

				while (itr != null && timestamps.Count < BlockTimestamps.SIZE)
				{
					timestamps.Add(itr.header.timestamp);
					itr = itr.header.parent.Length == 0 ? null : GetBlock(itr.header.parent);
				}
				Timestamps.Init(timestamps.ToArray());
			}
		}

		public TransactionContext GetDBTransaction()
		{
			return _DBContext.GetTransactionContext();
		}

		public AddBk.Result HandleNewBlock(Types.Block block, bool handleOrphan = false) //TODO: use Keyed type
		{
			var doActions = new List<Action>();
			var undoActions = new List<Action>();

			AddBk.Result result;

			lock (_lockObject)
			{
				using (TransactionContext context = _DBContext.GetTransactionContext())
				{
					result = new AddBk(
						this,
						context,
						new Keyed<Types.Block>(Merkle.blockHeaderHasher.Invoke(block.header), block),
						doActions,
						undoActions,
						_OrphansActions
					).Start(handleOrphan);

					if (result != AddBk.Result.Rejected)
					{
					//	TxMempool.Lock(() =>
					//	{
							context.Commit();
							foreach (Action action in doActions)
								action();
					//	});
					}
					else
					{
						foreach (Action action in undoActions)
							action();
					}

					BlockChainTrace.Information("Block " + System.Convert.ToBase64String(Merkle.blockHeaderHasher.Invoke(block.header)) + " is " + result);
				}
			}

			while (_OrphansActions.TryPop(out block))
			{
				HandleNewBlock(block, true);
			}

			return result;
		}

		public AddTx.Result HandleNewTransaction(Types.Transaction transaction) //TODO: use Keyed type
		{
			var doActions = new List<Action>();
			var undoActions = new List<Action>();

			using (TransactionContext context = _DBContext.GetTransactionContext())
			{
				var result = new AddTx(
					this,
					context,
					new Keyed<Types.Transaction>(Merkle.transactionHasher.Invoke(transaction), transaction),
					doActions,
					undoActions
				).Start();

				if (result != AddTx.Result.Rejected)
				{
					context.Commit(); //TODO: don't need to commit if added to mempool
					foreach (Action action in doActions)
						action();
				}
				else
				{
					foreach (Action action in undoActions)
						action();
				}

				return result;
			}
		}

		public bool HandleNewContract(Types.Contract contract)
		{
			using (TransactionContext context = _DBContext.GetTransactionContext())
			{
				var contractHash = ContractHelper.Compile(contract.code);

				if (contractHash != null)
				{
					ContractStore.Put(context, new Keyed<Types.Contract>(contractHash, contract));
					context.Commit();

					return true;
				}

				return false;
			}
		}

		public Types.Transaction GetTransaction(byte[] key) //TODO: make concurrent
		{
			if (TxMempool.ContainsKey(key))
			{
				return TransactionValidation.unpoint(TxMempool.Get(key));
			}
			else
			{
				using (TransactionContext context = _DBContext.GetTransactionContext())
				{
					if (BlockStore.TxStore.ContainsKey(context, key))
					{
						return BlockStore.TxStore.Get(context, key).Value;
					}
				}
			}

			return null;
		}

		//TODO: should asset that the block came from main?
		public Types.Block GetBlock(byte[] key)
		{
			using (TransactionContext context = _DBContext.GetTransactionContext())
			{
				var bk = BlockStore.GetBlock(context, key);

				return bk == null ? null : bk.Value;
			}
		}

		// TODO: use linq, return enumerator, remove predicate
		public Dictionary<Keyed<Types.Transaction>, Types.Output> GetUTXOSet(Func<Types.Output, bool> predicate)
		{
			var outputs = new HashDictionary<Types.Output>();
			var values = new Dictionary<Keyed<Types.Transaction>, Types.Output>();

			using (TransactionContext context = _DBContext.GetTransactionContext())
			{
				foreach (var output in UTXOStore.All(context, predicate, true))
				{
					byte[] txHash = new byte[output.Key.Length - 1];
					Array.Copy(output.Key, txHash, txHash.Length);
					outputs[txHash] = output.Value;
				}

				foreach (var output in outputs)
				{
					var tx = BlockStore.TxStore.Get(context, output.Key);
					values[tx] = output.Value;
				}
			}

			return values;
		}

		//public List<Tuple<Types.Outpoint,Types.Output>> GetUTXOSet()
		//{
		//	var values = new List<Tuple<Types.Outpoint, Types.Output>>();

		//	using (TransactionContext context = _DBContext.GetTransactionContext())
		//	{
		//		foreach (var item in _UTXOStore.All(context))
		//		{
		//			byte[] txHash = new byte[item.Key.Length - 1];
		//			Array.Copy(item.Key, txHash, txHash.Length);

		//			uint index = item.Key[item.Key.Length - 1];

		//			var outpoint = new Types.Outpoint(txHash, index);

		//			values.Add(new Tuple<Types.Outpoint, Types.Output>(outpoint, item.Value));
		//		}
		//	}

		//	return values;
		//}

		//demo
		public Types.Block MineAllInMempool()
		{
			var transactions = TxMempool.GetAll();

			if (transactions.Count == 0)
			{
				return null;
			}

			uint version = 1;
			string date = "2000-02-02";

		//	Merkle.Hashable x = new Merkle.Hashable ();
		//	x.
		//	var merkleRoot = Merkle.merkleRoot(Tip.Key,

			var nonce = new byte[10];

			new Random().NextBytes (nonce);

			var blockHeader = new Types.BlockHeader(
				version,
				Tip.Key,
				0,
				new byte[] { },
				new byte[] { },
				new byte[] { },
				ListModule.OfSeq<byte[]>(new List<byte[]>()),
				DateTime.Parse(date).ToBinary(),
				1,
				nonce
			);

			var newBlock = new Types.Block(blockHeader, ListModule.OfSeq<Types.Transaction>(transactions.Select(
				t => TransactionValidation.unpoint(t)))
          	);

			if (HandleNewBlock(newBlock) == AddBk.Result.Added)
			{
				return newBlock;
			}
			else 
			{
				throw new Exception();
			}
		}


		//private Double GetDifficultyRecursive(TransactionContext context, Types.Block block)
		//{
		//	Double result = block.header.pdiff;

		//	if (block.header.parent == null || block.header.parent.Length == 0)
		//	{
		//		return result;
		//	}

		//	Types.Block parentBlock = _BlockStore.Get(context, block.header.parent).Value;

		//	if (parentBlock == null)
		//	{
		//		throw new Exception("Missing parent block");
		//	}

		//	return result + GetDifficultyRecursive(context, parentBlock);
		//}
	}

	
}
