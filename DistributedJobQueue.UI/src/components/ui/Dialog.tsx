import { cn } from '../../utils/cn';
import { createContext, useContext, useState, ReactNode } from 'react';
import { X } from 'lucide-react';

interface DialogContextType {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

const DialogContext = createContext<DialogContextType | null>(null);

export const useDialogContext = () => {
  const context = useContext(DialogContext);
  if (!context) throw new Error('Dialog components must be used within DialogProvider');
  return context;
};

interface DialogProps {
  children: ReactNode;
  defaultOpen?: boolean;
}

export const DialogProvider = ({ children, defaultOpen = false }: DialogProps) => {
  const [open, setOpen] = useState(defaultOpen);

  return (
    <DialogContext.Provider value={{ open, onOpenChange: setOpen }}>
      {children}
    </DialogContext.Provider>
  );
};

export const DialogTrigger = ({ children, onClick, ...props }: React.ButtonHTMLAttributes<HTMLButtonElement> & { children: ReactNode }) => {
  const { onOpenChange } = useDialogContext();
  return (
    <button onClick={() => onOpenChange(true)} {...props}>
      {children}
    </button>
  );
};

interface DialogContentProps {
  children: ReactNode;
  className?: string;
}

export const DialogContent = ({ children, className, ...props }: DialogContentProps) => {
  const { open, onOpenChange } = useDialogContext();

  if (!open) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center" onClick={() => onOpenChange(false)}>
      <div className="fixed inset-0 bg-black/50" />
      <div
        className={cn(
          'relative z-50 w-full max-w-lg rounded-lg bg-background p-6 shadow-lg animate-in fade-in-0 zoom-in-95',
          className
        )}
        onClick={(e) => e.stopPropagation()}
        {...props}
      >
        {children}
        <button
          className="absolute right-4 top-4 rounded-sm opacity-70 ring-offset-background transition-opacity hover:opacity-100 focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2"
          onClick={() => onOpenChange(false)}
        >
          <X className="h-4 w-4" />
        </button>
      </div>
    </div>
  );
};

export const DialogHeader = ({ className, children, ...props }: React.HTMLAttributes<HTMLDivElement>) => (
  <div className={cn('flex flex-col space-y-1.5 text-center sm:text-left', className)} {...props}>
    {children}
  </div>
);

export const DialogTitle = ({ className, children, ...props }: React.HTMLAttributes<HTMLHeadingElement>) => (
  <h2 className={cn('text-lg font-semibold leading-none tracking-tight', className)} {...props}>
    {children}
  </h2>
);

export const DialogDescription = ({ className, children, ...props }: React.HTMLAttributes<HTMLParagraphElement>) => (
  <p className={cn('text-sm text-muted-foreground', className)} {...props}>
    {children}
  </p>
);

export const DialogFooter = ({ className, children, ...props }: React.HTMLAttributes<HTMLDivElement>) => (
  <div className={cn('flex flex-col-reverse sm:flex-row sm:justify-end sm:space-x-2', className)} {...props}>
    {children}
  </div>
);

export const Dialog = DialogProvider;