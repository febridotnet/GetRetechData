IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'item_loc_soh')
BEGIN
    CREATE TABLE item_loc_soh (
        [loc] NVARCHAR(MAX),
        [item] NVARCHAR(MAX),
        [stock_on_hand] NVARCHAR(MAX),
        [av_cost] NVARCHAR(MAX),
        [First_Received] NVARCHAR(MAX),
        [Last_Received] NVARCHAR(MAX),
        [First_sold] NVARCHAR(MAX),
        [Last_sold] NVARCHAR(MAX),
        [in_transit_qty] NVARCHAR(MAX),
        [soh_update_datetime] NVARCHAR(MAX),
        [last_update_datetime] NVARCHAR(MAX)
    )
END
