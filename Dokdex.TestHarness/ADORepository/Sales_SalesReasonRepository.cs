using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data.SqlClient;
using Dokdex.Library.Client;
using Dokdex.Library.Payloads;

namespace Dokdex.TestHarness.ADORepository
{
	public partial class Sales_SalesReasonRepository
	{        
		public void Export_Sales_SalesReason()
		{
            DokdexClient client = new DokdexClient("http://localhost:6858/");

            if(client.Schema.Exists("AdventureWorks2012:Sales:SalesReason"))
			{
				return;
			}

            client.Transaction.Begin();

            client.Schema.Create("AdventureWorks2012:Sales:SalesReason");

			using (SqlConnection connection = new SqlConnection("Server=.;Database=AdventureWorks2012;Trusted_Connection=True;"))
			{
				connection.Open();

				try
				{
					using (SqlCommand command = new SqlCommand("SELECT * FROM Sales.SalesReason", connection))
					{
						command.CommandTimeout = 10000;
						command.CommandType = System.Data.CommandType.Text;

						using (SqlDataReader dataReader = command.ExecuteReader(System.Data.CommandBehavior.CloseConnection))
						{
                            int indexOfSalesReasonID = dataReader.GetOrdinal("SalesReasonID");
						    int indexOfName = dataReader.GetOrdinal("Name");
						    int indexOfReasonType = dataReader.GetOrdinal("ReasonType");
						    int indexOfModifiedDate = dataReader.GetOrdinal("ModifiedDate");
						    
							int rowCount = 0;


							while (dataReader.Read() /*&& rowCount++ < 10000*/)
							{
								if(rowCount > 0 && (rowCount % 100) == 0)
								{
									Console.WriteLine("AdventureWorks2012:Sales:SalesReason: {0}", rowCount);
								}

								if(rowCount > 0 && (rowCount % 1000) == 0)
								{
									Console.WriteLine("Comitting...");
									client.Transaction.Commit();
									client.Transaction.Begin();
								}

								try
								{
									client.Document.Store("AdventureWorks2012:Sales:SalesReason", new Document(new Models.Sales_SalesReason
									{
											SalesReasonID= dataReader.GetInt32(indexOfSalesReasonID),
											Name= dataReader.GetString(indexOfName),
											ReasonType= dataReader.GetString(indexOfReasonType),
											ModifiedDate= dataReader.GetDateTime(indexOfModifiedDate),
										}));
								}
								catch(Exception ex)
								{
									Console.WriteLine(ex.Message);
								}
								
								rowCount++;
							}
						}
					}
					connection.Close();
				}
				catch
				{
					//TODO: add error handling/logging
					throw;
				}

				client.Transaction.Commit();
            }
		}
	}
}

