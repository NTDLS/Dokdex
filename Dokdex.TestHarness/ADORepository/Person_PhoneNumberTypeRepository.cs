using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data.SqlClient;
using Dokdex.Library.Client;
using Dokdex.Library.Payloads;

namespace Dokdex.TestHarness.ADORepository
{
	public partial class Person_PhoneNumberTypeRepository
	{        
		public void Export_Person_PhoneNumberType()
		{
            DokdexClient client = new DokdexClient("http://localhost:6858/");

            if(client.Schema.Exists("AdventureWorks2012:Person:PhoneNumberType"))
			{
				return;
			}

            client.Transaction.Begin();

            client.Schema.Create("AdventureWorks2012:Person:PhoneNumberType");

			using (SqlConnection connection = new SqlConnection("Server=.;Database=AdventureWorks2012;Trusted_Connection=True;"))
			{
				connection.Open();

				try
				{
					using (SqlCommand command = new SqlCommand("SELECT * FROM Person.PhoneNumberType", connection))
					{
						command.CommandTimeout = 10000;
						command.CommandType = System.Data.CommandType.Text;

						using (SqlDataReader dataReader = command.ExecuteReader(System.Data.CommandBehavior.CloseConnection))
						{
                            int indexOfPhoneNumberTypeID = dataReader.GetOrdinal("PhoneNumberTypeID");
						    int indexOfName = dataReader.GetOrdinal("Name");
						    int indexOfModifiedDate = dataReader.GetOrdinal("ModifiedDate");
						    
							int rowCount = 0;


							while (dataReader.Read() /*&& rowCount++ < 10000*/)
							{
								if(rowCount > 0 && (rowCount % 100) == 0)
								{
									Console.WriteLine("AdventureWorks2012:Person:PhoneNumberType: {0}", rowCount);
								}

								if(rowCount > 0 && (rowCount % 1000) == 0)
								{
									Console.WriteLine("Comitting...");
									client.Transaction.Commit();
									client.Transaction.Begin();
								}

								try
								{
									client.Document.Store("AdventureWorks2012:Person:PhoneNumberType", new Document(new Models.Person_PhoneNumberType
									{
											PhoneNumberTypeID= dataReader.GetInt32(indexOfPhoneNumberTypeID),
											Name= dataReader.GetString(indexOfName),
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

