using System;
using System.Runtime.Serialization;

namespace Dokdex.TestHarness.Models
{
	public partial class Production_ProductCategory
	{
		#region Properties
		private int _productCategoryID;
		public int ProductCategoryID
		{
			get
			{
				return this._productCategoryID;
			}
			set
			{
				if (this._productCategoryID != value)
				{
					this._productCategoryID = value;
				}            
			}
		}
		private string _name;
		public string Name
		{
			get
			{
				return this._name;
			}
			set
			{
				if (this._name != value)
				{
					this._name = value;
				}            
			}
		}
		private Guid _rowguid;
		public Guid rowguid
		{
			get
			{
				return this._rowguid;
			}
			set
			{
				if (this._rowguid != value)
				{
					this._rowguid = value;
				}            
			}
		}
		private DateTime _modifiedDate;
		public DateTime ModifiedDate
		{
			get
			{
				return this._modifiedDate;
			}
			set
			{
				if (this._modifiedDate != value)
				{
					this._modifiedDate = value;
				}            
			}
		}
			
		#endregion
	}
}
