'use client';

import React, { createContext, useContext, useState, useEffect, ReactNode } from 'react';

interface TableContextData {
  tableId: string | null;
  tableNumber: string | null;
  qrScanned: boolean;
  isOutdoor: boolean;
}

interface TableContextType {
  tableContext: TableContextData;
  setTableContext: (data: Partial<TableContextData>) => void;
  clearTableContext: () => void;
  hasTableContext: boolean;
}

const TableContext = createContext<TableContextType | undefined>(undefined);

const STORAGE_KEY = 'rumi_table_context';

export function TableContextProvider({ children }: { children: ReactNode }) {
  const [tableContext, setTableContextState] = useState<TableContextData>({
    tableId: null,
    tableNumber: null,
    qrScanned: false,
    isOutdoor: false,
  });

  // Load from session storage on mount
  useEffect(() => {
    try {
      const stored = sessionStorage.getItem(STORAGE_KEY);
      if (stored) {
        const parsed = JSON.parse(stored);
        setTableContextState(parsed);
      }
    } catch {
      // Invalid storage data, ignore
    }
  }, []);

  // Save to session storage whenever context changes
  useEffect(() => {
    if (tableContext.tableId) {
      sessionStorage.setItem(STORAGE_KEY, JSON.stringify(tableContext));
    } else {
      sessionStorage.removeItem(STORAGE_KEY);
    }
  }, [tableContext]);

  const setTableContext = (data: Partial<TableContextData>) => {
    setTableContextState((prev) => ({
      ...prev,
      ...data,
    }));
  };

  const clearTableContext = () => {
    setTableContextState({
      tableId: null,
      tableNumber: null,
      qrScanned: false,
      isOutdoor: false,
    });
    sessionStorage.removeItem(STORAGE_KEY);
  };

  const hasTableContext = Boolean(tableContext.tableId);

  return (
    <TableContext.Provider
      value={{
        tableContext,
        setTableContext,
        clearTableContext,
        hasTableContext,
      }}
    >
      {children}
    </TableContext.Provider>
  );
}

export function useTableContext() {
  const context = useContext(TableContext);
  if (context === undefined) {
    throw new Error('useTableContext must be used within a TableContextProvider');
  }
  return context;
}
