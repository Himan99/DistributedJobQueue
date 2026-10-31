import { cn } from '../../utils/cn';
import { createContext, useContext, useState, ReactNode, isValidElement, cloneElement } from 'react';
import { ChevronDown } from 'lucide-react';

interface DropdownMenuContextType {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

const DropdownMenuContext = createContext<DropdownMenuContextType | null>(null);

const useDropdownMenuContext = () => {
  const context = useContext(DropdownMenuContext);
  if (!context) throw new Error('DropdownMenu components must be used within DropdownMenu');
  return context;
};

export const DropdownMenu = ({ children }: { children: ReactNode }) => {
  const [open, setOpen] = useState(false);
  return (
    <DropdownMenuContext.Provider value={{ open, onOpenChange: setOpen }}>
      <div className="relative inline-block">{children}</div>
    </DropdownMenuContext.Provider>
  );
};

export const DropdownMenuTrigger = ({
  children,
  className,
  asChild,
  ...props
}: React.ButtonHTMLAttributes<HTMLButtonElement> & { children: ReactNode; asChild?: boolean }) => {
  const { open, onOpenChange } = useDropdownMenuContext();

  const renderContent = () => {
    if (asChild && isValidElement(children)) {
      return cloneElement(children as React.ReactElement, {
        onClick: (e: React.MouseEvent) => { e.stopPropagation(); onOpenChange(!open); },
        'aria-expanded': open,
        ...props,
      });
    }

    return (
      <button
        className={cn(
          'inline-flex items-center justify-center gap-1.5 rounded-md bg-primary px-3 py-2 text-sm font-medium text-primary-foreground hover:bg-primary/90',
          className
        )}
        onClick={(e) => { e.stopPropagation(); onOpenChange(!open); }}
        aria-expanded={open}
        {...props}
      >
        {children}
        <ChevronDown className={cn('h-4 w-4 transition-transform', open && 'rotate-180')} />
      </button>
    );
  };

  return <>{renderContent()}</>;
};

export const DropdownMenuContent = ({
  children,
  className,
  ...props
}: React.HTMLAttributes<HTMLDivElement>) => {
  const { open } = useDropdownMenuContext();
  if (!open) return null;

  return (
    <div
      className={cn(
        'z-50 min-w-[8rem] origin-top-right rounded-md bg-white dark:bg-gray-900 text-gray-900 dark:text-white p-1 shadow-md animate-in fade-in-0 zoom-in-95 border border-gray-200 dark:border-gray-700',
        className
      )}
      {...props}
    >
      {children}
    </div>
  );
};

export const DropdownMenuItem = ({
  children,
  className,
  onClick,
  disabled,
  ...props
}: React.ButtonHTMLAttributes<HTMLButtonElement> & { children: ReactNode }) => {
  const { onOpenChange } = useDropdownMenuContext();
  return (
    <button
      className={cn(
        'flex w-full items-center rounded-sm px-2 py-1.5 text-sm outline-none text-gray-900 dark:text-white focus:bg-gray-100 dark:focus:bg-gray-800 data-[disabled]:pointer-events-none data-[disabled]:opacity-50',
        className
      )}
      onClick={(e) => { if (!disabled) { onOpenChange(false); onClick?.(e); } }}
      disabled={disabled}
      {...props}
    >
      {children}
    </button>
  );
};

export const DropdownMenuSeparator = ({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) => (
  <div className={cn('border-t border-gray-200 dark:border-gray-700 -mx-1 my-1', className)} {...props} />
);

export const DropdownMenuLabel = ({ className, children, ...props }: React.HTMLAttributes<HTMLDivElement>) => (
  <div className={cn('px-2 py-1.5 text-sm font-semibold text-gray-900 dark:text-white', className)} {...props}>
    {children}
  </div>
);